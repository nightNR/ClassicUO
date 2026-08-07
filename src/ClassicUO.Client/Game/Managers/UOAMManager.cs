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
        private const uint ReconnectDelayMs = 5000;

        private readonly World _world;
        private readonly UOAMClient _client = new UOAMClient();
        private bool _wasInGame;
        private bool _manualDisconnect;
        private uint _nextPositionSend, _nextChatPoll, _nextListQuery;
        private uint _reconnectAt;

        public UOAMManager(World world)
        {
            _world = world;
            Players = new UOAMPlayerManager();

            _client.PlayerListReceived += list => Players.UpdateFromList(list);
            _client.ChatTextReceived += (sender, text) => ChatTextReceived?.Invoke(sender, text);
            _client.Connected += () =>
            {
                GameActions.Print(_world, "UOAM: connected", 0x2B);

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
                }
                else
                {
                    // Connection dropped unexpectedly (idle timeout, network blip, the
                    // game window losing focus and throttling the update loop long
                    // enough for the server's own idle timeout to fire, ...) - retry
                    // instead of silently sitting disconnected until the player notices.
                    GameActions.Print(_world, "UOAM: connection lost, retrying...", 0x2B);
                    _reconnectAt = Time.Ticks + ReconnectDelayMs;
                }
            };
        }

        public UOAMPlayerManager Players { get; }

        public string TrackedPlayerName { get; set; }

        public bool IsConnected => _client.IsConnected;

        public bool IsConnecting => _client.IsConnecting;

        public event Action<string, string> ChatTextReceived;

        public void Connect()
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
            _nextPositionSend = 0;
            _nextChatPoll = 0;
            _nextListQuery = 0;

            _client.Connect(profile.UOAMServerIP, profile.UOAMServerPort);
        }

        public void Disconnect()
        {
            bool wasActive = _client.IsConnected || _client.IsConnecting;

            _manualDisconnect = true;
            _reconnectAt = 0;
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

            if (inGame && !_wasInGame && ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.UOAMAutoConnect)
            {
                Connect();
            }
            else if (!inGame && _wasInGame)
            {
                Disconnect();
            }

            _wasInGame = inGame;

            if (!inGame)
            {
                return;
            }

            if (_reconnectAt != 0 && Time.Ticks >= _reconnectAt && !_client.IsConnected && !_client.IsConnecting)
            {
                _reconnectAt = 0;
                Connect();
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

            if (now >= _nextChatPoll)
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

            if (profile == null || !_client.IsConnected)
            {
                return;
            }

            _client.SendChatText(profile.UOAMPassword, profile.UOAMPlayerName, text);
        }
    }
}
