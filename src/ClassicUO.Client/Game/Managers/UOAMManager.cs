// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using ClassicUO.Network.UOAM;

namespace ClassicUO.Game.Managers
{
    internal sealed class UOAMManager
    {
        private const uint PositionIntervalMs = 2000;
        private const uint ChatPollIntervalMs = 1500;
        private const uint ListQueryIntervalMs = 3000;

        private readonly World _world;
        private readonly UOAMClient _client = new UOAMClient();
        private bool _wasInGame;
        private uint _nextPositionSend, _nextChatPoll, _nextListQuery;

        public UOAMManager(World world)
        {
            _world = world;
            Players = new UOAMPlayerManager();

            _client.PlayerListReceived += list => Players.UpdateFromList(list);
            _client.ChatTextReceived += text => ChatTextReceived?.Invoke(text);
            _client.Connected += () => GameActions.Print(_world, "UOAM: connected", 0x2B);
            _client.Disconnected += () =>
            {
                Players.Clear();
                GameActions.Print(_world, "UOAM: disconnected", 0x2B);
            };
        }

        public UOAMPlayerManager Players { get; }

        public string TrackedPlayerName { get; set; }

        public bool IsConnected => _client.IsConnected;

        public event Action<string> ChatTextReceived;

        public void Connect()
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || string.IsNullOrEmpty(profile.UOAMServerIP))
            {
                return;
            }

            _nextPositionSend = 0;
            _nextChatPoll = 0;
            _nextListQuery = 0;

            _client.Connect(profile.UOAMServerIP, profile.UOAMServerPort);
        }

        public void Disconnect()
        {
            _client.Disconnect();
            Players.Clear();
            TrackedPlayerName = null;
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
                _client.SendChatPoll(profile.UOAMPassword);
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

            _client.SendChatText(profile.UOAMPassword, text);
        }
    }
}
