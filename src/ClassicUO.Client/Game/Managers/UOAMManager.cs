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

        private readonly World _world;
        private readonly UOAMClient _client = new UOAMClient();
        private bool _wasInGame;
        private bool _manualDisconnect;
        private uint _nextPositionSend, _nextListQuery;
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

        public bool IsConnected => _client.IsConnected;

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
            _reconnectAt = 0;
            _connectedAt = 0;
            _nextPositionSend = 0;
            _nextListQuery = 0;

            _client.Connect(profile.UOAMServerIP, profile.UOAMServerPort);
        }

        public void Disconnect()
        {
            bool wasActive = _client.IsConnected || _client.IsConnecting;

            _manualDisconnect = true;
            _reconnectAt = 0;
            _reconnectAttempt = 0;
            _connectedAt = 0;
            _client.Disconnect();
            Players.Clear();
            TrackedPlayerName = null;

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

            // Chat (registration, poll, and send) is currently known-broken - every
            // chat-family request this client sends gets an RPC fault from the server,
            // even byte-identical replays of requests that succeeded in a real client's
            // own capture (see docs/uoam-protocol.md's Status section). Automatic chat
            // polling used to fire unconditionally every ChatPollIntervalMs for the
            // entire session regardless of whether chat was even in use, hammering the
            // server with a guaranteed fault every 1.5s during ordinary position-only
            // play - and disconnects kept happening during plain in-game use even after
            // the facet-switch and backoff fixes, which this may well explain.
            // Deliberately not calling _client.SendChatPoll here until the underlying
            // RPC issue is understood; re-enabling it without fixing that first would
            // resume the same nonstop-fault spam.

            if (now >= _nextListQuery)
            {
                _nextListQuery = now + ListQueryIntervalMs;
                _client.SendQueryList(profile.UOAMPassword);
            }
        }

        public void SendChatText(string text)
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || !_client.IsConnected)
            {
                return;
            }

            _client.SendChatText(profile.UOAMPassword, profile.UOAMPlayerName, text);
        }
    }
}
