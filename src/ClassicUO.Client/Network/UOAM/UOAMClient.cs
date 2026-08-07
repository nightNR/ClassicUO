// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Net;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network.UOAM
{
    // Owns two UOAMSocketChannel connections, matching how a real client behaves (see
    // docs/uoam-protocol.md): a primary connection for position updates and the player
    // list, and a second, separately-handshaken connection dedicated entirely to chat.
    // Every chat operation attempted on the primary connection alone - byte-identical to
    // real captured requests - gets an RPC fault from the server; only requests sent on
    // a properly bound secondary connection succeed.
    internal sealed class UOAMClient
    {
        private readonly byte[] _ipMarker = new byte[4];
        private readonly UOAMSocketChannel _primary = new UOAMSocketChannel();
        private readonly UOAMSocketChannel _chat = new UOAMSocketChannel();

        private string _connectIp;
        private ushort _connectPort;
        private uint _primarySequence;
        private uint _chatSequence;
        private uint _handshakeClientId;
        private bool _handshakeAcked;
        private bool _chatReady;

        public bool IsConnected => _handshakeAcked && _primary.IsConnected;
        public bool IsConnecting => _primary.IsConnecting;
        public bool IsChatReady => _chatReady && _chat.IsConnected;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<List<UOAMPlayerInfo>> PlayerListReceived;
        public event Action<string, string> ChatTextReceived;

        public UOAMClient()
        {
            _primary.Connected += OnPrimaryConnected;
            _primary.Disconnected += OnPrimaryDisconnected;
            _primary.FrameReceived += OnPrimaryFrame;

            _chat.Connected += OnChatConnected;
            _chat.Disconnected += OnChatDisconnected;
            _chat.FrameReceived += OnChatFrame;
        }

        public void Connect(string ip, ushort port)
        {
            Disconnect();

            _connectIp = ip;
            _connectPort = port;
            _handshakeAcked = false;
            _chatReady = false;
            _handshakeClientId = 0;
            _primarySequence = 0;
            _chatSequence = 0;
            Array.Clear(_ipMarker, 0, _ipMarker.Length);

            _primary.Connect(ip, port);
        }

        public void Disconnect()
        {
            _primary.Disconnect();
            _chat.Disconnect();
            _handshakeAcked = false;
            _chatReady = false;
        }

        public void Update()
        {
            _primary.Update();
            _chat.Update();
        }

        private void OnPrimaryConnected()
        {
            UpdateLocalIpMarker();
            _primary.Send(UOAMProtocol.BuildHandshake(++_primarySequence));
        }

        private void OnPrimaryDisconnected()
        {
            _handshakeAcked = false;
            _chatReady = false;
            _chat.Disconnect();
            Disconnected?.Invoke();
        }

        private void OnChatConnected()
        {
            _chat.Send(UOAMProtocol.BuildChatSocketHandshake(++_chatSequence, _handshakeClientId));
        }

        private void OnChatDisconnected()
        {
            // Chat is a bonus channel - losing it should not tear down position/list,
            // which keeps working fine on the primary connection regardless. Just mark
            // it not-ready; the caller can decide whether/when to retry the chat socket.
            _chatReady = false;
        }

        private void UpdateLocalIpMarker()
        {
            try
            {
                if (_primary.LocalEndPoint is IPEndPoint ep)
                {
                    byte[] bytes = ep.Address.MapToIPv4().GetAddressBytes();

                    if (bytes.Length == 4)
                    {
                        bytes.CopyTo(_ipMarker, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"UOAM: failed to read local IP: {ex}");
            }
        }

        public void SendPositionUpdate(string password, string name, int x, int y, int z, int map)
        {
            if (!IsConnected)
            {
                return;
            }

            _primary.Send(UOAMProtocol.BuildPositionUpdate(++_primarySequence, password, _ipMarker, name, x, y, z, map));
        }

        public void SendQueryList(string password)
        {
            if (!IsConnected)
            {
                return;
            }

            _primary.Send(UOAMProtocol.BuildQueryListRequest(++_primarySequence, password, _ipMarker));
        }

        public void SendChatPoll(string password, string playerName)
        {
            if (!IsChatReady)
            {
                return;
            }

            _chat.Send(UOAMProtocol.BuildChatPollRequest(++_chatSequence, password, _ipMarker, playerName));
        }

        public void SendChatText(string password, string playerName, string text)
        {
            if (!IsChatReady)
            {
                return;
            }

            _chat.Send(UOAMProtocol.BuildChatSendRequest(++_chatSequence, password, _ipMarker, playerName, text));
        }

        private void OnPrimaryFrame(byte[] data)
        {
            switch (UOAMProtocol.Classify(data))
            {
                case UOAMResponseKind.HandshakeAck:
                    if (UOAMProtocol.TryParseHandshakeAck(data, out uint clientId))
                    {
                        _handshakeClientId = clientId;
                    }

                    // Real clients always send this immediately after the handshake ack,
                    // before any position/list traffic - see UOAMProtocol's
                    // MysteryPacketTemplate comment.
                    _primary.Send(UOAMProtocol.BuildMysteryFollowup(++_primarySequence, _handshakeClientId));

                    _handshakeAcked = true;
                    Connected?.Invoke();

                    // Chat only works over its own dedicated, separately-handshaken
                    // connection - open it now that we have the primary connection's
                    // client id to bind it to.
                    _chat.Connect(_connectIp, _connectPort);
                    break;

                case UOAMResponseKind.PlayerList:
                    var players = new List<UOAMPlayerInfo>();
                    UOAMProtocol.TryParsePlayerList(data, players);
                    PlayerListReceived?.Invoke(players);
                    break;

                case UOAMResponseKind.Ack:
                case UOAMResponseKind.NoChat:
                case UOAMResponseKind.ChatData:
                case UOAMResponseKind.Unknown:
                    break; // nothing to do (chat-shaped responses never arrive on primary)
            }
        }

        private void OnChatFrame(byte[] data)
        {
            switch (UOAMProtocol.Classify(data))
            {
                case UOAMResponseKind.HandshakeAck:
                    _chatReady = true;
                    break;

                case UOAMResponseKind.ChatData:
                    if (UOAMProtocol.TryParseChatData(data, out byte subtype, out string senderName, out string text) && subtype == 0x01 && text != null)
                    {
                        ChatTextReceived?.Invoke(senderName, text);
                    }
                    break;

                case UOAMResponseKind.Ack:
                case UOAMResponseKind.NoChat:
                case UOAMResponseKind.PlayerList:
                case UOAMResponseKind.Unknown:
                    break; // nothing to do
            }
        }
    }
}
