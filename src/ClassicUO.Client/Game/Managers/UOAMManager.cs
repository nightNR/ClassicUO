// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Network.UOAM;

namespace ClassicUO.Game.Managers
{
    internal sealed class UOAMManager
    {
        private const uint PositionIntervalMs = 2000;
        private const uint ChatPollIntervalMs = 1500;
        private const uint ListQueryIntervalMs = 3000;
        private const uint ReconnectBaseDelayMs = 5000;
        private const uint ReconnectMaxDelayMs = 60000;
        private const int MaxReconnectAttempts = 5;
        // A live capture showed the server FIN+RST-ing several reconnect attempts in a
        // row within ~120ms of the handshake completing, each one still counted as a
        // "success" by the old code (which reset the backoff to attempt 1 the instant
        // Connected fired). That made the exponential backoff never actually escalate -
        // it kept retrying every ~5s indefinitely, which likely looked like abuse to
        // the server and made things worse. Only reset the backoff once a connection
        // has stayed up for a real stretch, not merely completed its handshake.
        private const uint SustainedConnectionMs = 20000;

        // A real UOAM client's own "disconnect" button does not close its TCP
        // connection at all - it just stops sending position updates and lets the
        // hub's own staleness check drop it from other clients' player lists, which a
        // live capture showed takes about 5 seconds. Closing the connection outright
        // on disconnect (what this client used to do) was observed to make the hub
        // take up to a minute to release the player-name slot instead - apparently a
        // slower cleanup path than idle expiry - which then made an immediate
        // reconnect under the same name fault (see docs/uoam-protocol.md "Faults").
        // Mimic the real client: stop sending immediately, but only actually tear
        // down the socket after this much idle time, long enough for the hub's own
        // TTL to have already dropped us everywhere else.
        private const uint DisconnectLingerMs = 8000;

        private readonly World _world;
        private readonly UOAMClient _client = new UOAMClient();
        private bool _wasInGame;
        private bool _manualDisconnect;
        private bool _lingering;
        private uint _lingerUntil;
        private uint _nextPositionSend, _nextChatPoll, _nextListQuery;
        private uint _reconnectAt;
        private int _reconnectAttempt;
        private uint _connectedAt;

        public UOAMManager(World world)
        {
            _world = world;
            Players = new UOAMPlayerManager();

            _client.PlayerListReceived += list => Players.UpdateFromList(list);
            _client.ChatTextReceived += (sender, text) => ChatTextReceived?.Invoke(sender, text);
            _client.Connected += () =>
            {
                GameActions.Print(_world, "UOAM: connected", 0x2B);
                _connectedAt = Time.Ticks;

                if (ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.UOAMAutoOpenChat && UIManager.GetGump<UOAMChatGump>() == null)
                {
                    UIManager.Add(new UOAMChatGump(_world));
                }
            };
            _client.Disconnected += () =>
            {
                Players.Clear();

                if (_manualDisconnect)
                {
                    GameActions.Print(_world, "UOAM: disconnected", 0x2B);

                    return;
                }

                // A connection that stayed up for a real stretch earns a fresh backoff
                // sequence; one that died almost immediately after connecting (see the
                // SustainedConnectionMs comment above) does not - it keeps escalating.
                if (_connectedAt != 0 && Time.Ticks - _connectedAt >= SustainedConnectionMs)
                {
                    _reconnectAttempt = 0;
                }

                _connectedAt = 0;

                // Connection dropped unexpectedly (idle timeout, network blip, the
                // server actively closing it, ...) - retry with exponential backoff
                // instead of either sitting silently disconnected or hammering the
                // server with immediate reconnects (which can itself look like abuse
                // and get further attempts rejected outright).
                _reconnectAttempt++;

                if (_reconnectAttempt > MaxReconnectAttempts)
                {
                    GameActions.Print(_world, $"UOAM: connection lost, giving up after {MaxReconnectAttempts} attempts - reconnect manually", 0x2B);

                    return;
                }

                uint delay = (uint)Math.Min(ReconnectBaseDelayMs * (1u << (_reconnectAttempt - 1)), ReconnectMaxDelayMs);
                GameActions.Print(_world, $"UOAM: connection lost, retrying in {delay / 1000}s (attempt {_reconnectAttempt}/{MaxReconnectAttempts})...", 0x2B);
                _reconnectAt = Time.Ticks + delay;
            };
        }

        public UOAMPlayerManager Players { get; }

        public string TrackedPlayerName { get; set; }

        public bool IsConnected => !_lingering && _client.IsConnected;

        public bool IsConnecting => _client.IsConnecting;

        public event Action<string, string> ChatTextReceived;

        public void Connect()
        {
            // A fresh (manual, or autoconnect-on-login) request always gets a full set
            // of retries, independent of however many backoff attempts a previous
            // connection's failure sequence had already used up.
            _reconnectAttempt = 0;
            ConnectInternal();
        }

        private void ConnectInternal()
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || string.IsNullOrEmpty(profile.UOAMServerIP))
            {
                GameActions.Print(_world, "UOAM: no server IP configured", 0x2B);

                return;
            }

            GameActions.Print(_world, $"UOAM: connecting to {profile.UOAMServerIP}:{profile.UOAMServerPort}...", 0x2B);

            _manualDisconnect = false;
            _lingering = false;
            _reconnectAt = 0;
            _connectedAt = 0;
            _nextPositionSend = 0;
            _nextChatPoll = 0;
            _nextListQuery = 0;

            // UOAMClient.Connect() tears down any existing connection first (including
            // one still lingering from a recent Disconnect()), so no separate teardown
            // is needed here even if we're reconnecting before the linger finished.
            _client.Connect(profile.UOAMServerIP, profile.UOAMServerPort);
        }

        public void Disconnect()
        {
            bool wasActive = IsConnected || _client.IsConnecting;

            _manualDisconnect = true;
            _reconnectAt = 0;
            _reconnectAttempt = 0;
            _connectedAt = 0;
            Players.Clear();
            TrackedPlayerName = null;

            if (_client.IsConnected)
            {
                // See DisconnectLingerMs above - stop advertising ourselves now, but
                // leave the socket open for a few seconds so the hub's own idle-based
                // cleanup (not our TCP close) is what removes us from other clients'
                // player lists.
                _lingering = true;
                _lingerUntil = Time.Ticks + DisconnectLingerMs;
            }
            else
            {
                _client.Disconnect();
            }

            if (wasActive)
            {
                GameActions.Print(_world, "UOAM: disconnected", 0x2B);
            }
        }

        public void Update()
        {
            bool inGame = _world.InGame;

            // World.MapIndex's setter briefly nils out World.Map while switching facets
            // (recall, moongate, ...), which makes InGame (Player != null && Map != null)
            // go false for a frame or two even though the player never actually logged
            // out. Use Player == null - not InGame - as the "really logged out" signal
            // here, so a facet change mid-session doesn't tear down the UOAM connection.
            // World.Clear() already calls Disconnect() explicitly on the real logout path.
            bool reallyLoggedOut = _world.Player == null;

            if (inGame && !_wasInGame && ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.UOAMAutoConnect)
            {
                Connect();
            }
            else if (reallyLoggedOut && _wasInGame)
            {
                Disconnect();
            }

            _wasInGame = inGame;

            if (_lingering)
            {
                // Keep pumping so any late incoming data (or a fault) is drained rather
                // than left buffered, but send nothing further - see DisconnectLingerMs.
                // Runs even while reallyLoggedOut, since Disconnect() (called from
                // World.Clear() on a real logout) starts the same linger.
                _client.Update();

                if (Time.Ticks >= _lingerUntil)
                {
                    _lingering = false;
                    _client.Disconnect();
                }

                return;
            }

            if (reallyLoggedOut)
            {
                return;
            }

            if (!inGame)
            {
                // Transient (e.g. mid-facet-switch): skip this tick's sends but keep the
                // connection and pump the socket so incoming data isn't left buffered.
                _client.Update();

                return;
            }

            if (_reconnectAt != 0 && Time.Ticks >= _reconnectAt && !_client.IsConnected && !_client.IsConnecting)
            {
                _reconnectAt = 0;
                ConnectInternal();
            }

            _client.Update();

            if (!_client.IsConnected)
            {
                return;
            }

            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null)
            {
                return;
            }

            uint now = Time.Ticks;

            if (now >= _nextPositionSend)
            {
                _nextPositionSend = now + PositionIntervalMs;
                _client.SendPositionUpdate(profile.UOAMPassword, profile.UOAMPlayerName, _world.Player.X, _world.Player.Y, _world.Player.Z, _world.MapIndex);
            }

            // Chat lives on its own dedicated connection (UOAMClient opens and
            // handshakes it automatically once the primary connection is up - see
            // docs/uoam-protocol.md). Only poll once that secondary connection has
            // actually finished its own handshake; polling before then would just be
            // silently dropped by UOAMClient anyway (IsChatReady guards it there too),
            // but there's no point resetting the timer until it can actually succeed.
            if (_client.IsChatReady && now >= _nextChatPoll)
            {
                _nextChatPoll = now + ChatPollIntervalMs;
                _client.SendChatPoll(profile.UOAMPassword, profile.UOAMPlayerName);
            }

            if (now >= _nextListQuery)
            {
                _nextListQuery = now + ListQueryIntervalMs;
                _client.SendQueryList(profile.UOAMPassword);
            }
        }

        public void SendChatText(string text)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || !IsConnected)
            {
                return;
            }

            _client.SendChatText(profile.UOAMPassword, profile.UOAMPlayerName, text);
        }
    }
}
