// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network.UOAM
{
    // Owns three UOAMSocketChannel connections, matching how a real client behaves (see
    // docs/uoam-protocol.md): two redundant, independently-handshaken connections that
    // both carry position updates and query-list requests, and a third connection
    // dedicated entirely to chat. Every chat operation attempted on a position/list
    // connection - byte-identical to real captured requests - gets an RPC fault from the
    // server; only requests sent on a properly bound chat connection succeed.
    //
    // The two position/list legs are full duplicates of each other (same handshake,
    // same position updates, same query-list requests, both sent every tick) rather than
    // one primary plus a cold standby - that's what a real client does (see "Redundant
    // position connection" in the docs), and it means a single leg faulting or getting
    // RST'd (which has been observed to happen occasionally, seemingly server-side and
    // not tied to anything wrong with the request) doesn't interrupt visibility at all -
    // the other leg keeps the player on everyone's map while the dropped one silently
    // reopens in the background. The whole session is only considered lost once both
    // legs are down at once.
    internal sealed class UOAMClient
    {
        // A leg that just faulted or got reset shouldn't be redialed instantly - give
        // the server (and the network) a moment, mirroring the spirit of
        // UOAMManager's reconnect backoff without duplicating its longer schedule here
        // (this is for transient single-leg blips, not a fully dead session).
        private const long LegReconnectDelayMs = 3000;

        // One independently-handshaken position/list connection. Two of these run in
        // parallel - see the class comment.
        private sealed class Leg
        {
            public readonly UOAMSocketChannel Channel;
            public uint Sequence;
            public uint HandshakeClientId;
            public bool HandshakeAcked;
            public long ReconnectAt;

            public Leg(string name)
            {
                Channel = new UOAMSocketChannel(name);
            }

            public bool IsReady => HandshakeAcked && Channel.IsConnected;
        }

        private readonly byte[] _ipMarker = new byte[4];
        private readonly Leg _leg1 = new Leg("primary");
        private readonly Leg _leg2 = new Leg("primary2");
        private readonly UOAMSocketChannel _chat = new UOAMSocketChannel("chat");

        private string _connectIp;
        private ushort _connectPort;
        private uint _chatSequence;
        private bool _chatReady;
        private bool _chatOpened;

        public bool IsConnected => _leg1.IsReady || _leg2.IsReady;
        public bool IsConnecting => _leg1.Channel.IsConnecting || _leg2.Channel.IsConnecting;
        public bool IsChatReady => _chatReady && _chat.IsConnected;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<List<UOAMPlayerInfo>> PlayerListReceived;
        public event Action<string, string> ChatTextReceived;

        public UOAMClient()
        {
            _leg1.Channel.Connected += () => OnLegConnected(_leg1);
            _leg1.Channel.Disconnected += () => OnLegDisconnected(_leg1);
            _leg1.Channel.FrameReceived += data => OnLegFrame(_leg1, data);

            _leg2.Channel.Connected += () => OnLegConnected(_leg2);
            _leg2.Channel.Disconnected += () => OnLegDisconnected(_leg2);
            _leg2.Channel.FrameReceived += data => OnLegFrame(_leg2, data);

            _chat.Connected += OnChatConnected;
            _chat.Disconnected += OnChatDisconnected;
            _chat.FrameReceived += OnChatFrame;
        }

        public void Connect(string ip, ushort port)
        {
            Disconnect();

            _connectIp = ip;
            _connectPort = port;
            _chatReady = false;
            _chatOpened = false;
            ResetLeg(_leg1);
            ResetLeg(_leg2);
            _chatSequence = 0;
            Array.Clear(_ipMarker, 0, _ipMarker.Length);

            _leg1.Channel.Connect(ip, port);
            _leg2.Channel.Connect(ip, port);
        }

        private static void ResetLeg(Leg leg)
        {
            leg.Sequence = 0;
            leg.HandshakeClientId = 0;
            leg.HandshakeAcked = false;
            leg.ReconnectAt = 0;
        }

        public void Disconnect()
        {
            _leg1.Channel.Disconnect();
            _leg2.Channel.Disconnect();
            _chat.Disconnect();
            _leg1.HandshakeAcked = false;
            _leg2.HandshakeAcked = false;
            _chatReady = false;
        }

        public void Update()
        {
            _leg1.Channel.Update();
            _leg2.Channel.Update();
            _chat.Update();

            long now = Environment.TickCount64;
            TryReconnectLeg(_leg1, now);
            TryReconnectLeg(_leg2, now);
        }

        private void TryReconnectLeg(Leg leg, long now)
        {
            if (leg.ReconnectAt != 0 && now >= leg.ReconnectAt && !leg.Channel.IsConnected && !leg.Channel.IsConnecting)
            {
                leg.ReconnectAt = 0;
                leg.Channel.Connect(_connectIp, _connectPort);
            }
        }

        private void OnLegConnected(Leg leg)
        {
            UpdateLocalIpMarker();
            leg.Channel.Send(UOAMProtocol.BuildHandshake(++leg.Sequence));
        }

        private void OnLegDisconnected(Leg leg)
        {
            leg.HandshakeAcked = false;

            if (IsConnected)
            {
                // The other leg is still up - this is exactly the scenario the second
                // leg exists for. Quietly redial the one that dropped after a short
                // delay instead of tearing down the whole session.
                leg.ReconnectAt = Environment.TickCount64 + LegReconnectDelayMs;
                return;
            }

            // Both legs are down - the session really is gone.
            _chatReady = false;
            _chatOpened = false;
            _chat.Disconnect();
            Disconnected?.Invoke();
        }

        private void OnChatConnected()
        {
            uint masterId = _leg1.HandshakeAcked ? _leg1.HandshakeClientId : _leg2.HandshakeClientId;
            _chat.Send(UOAMProtocol.BuildChatSocketHandshake(++_chatSequence, masterId));
        }

        private void OnChatDisconnected()
        {
            // Chat is a bonus channel - losing it should not tear down position/list,
            // which keeps working fine on the remaining leg(s) regardless. Just mark
            // it not-ready; the caller can decide whether/when to retry the chat socket.
            _chatReady = false;
        }

        private void UpdateLocalIpMarker()
        {
            try
            {
                EndPoint local = _leg1.Channel.LocalEndPoint ?? _leg2.Channel.LocalEndPoint;

                if (local is IPEndPoint ep)
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
            if (_leg1.IsReady)
            {
                _leg1.Channel.Send(UOAMProtocol.BuildPositionUpdate(++_leg1.Sequence, password, _ipMarker, name, x, y, z, map));
            }

            if (_leg2.IsReady)
            {
                _leg2.Channel.Send(UOAMProtocol.BuildPositionUpdate(++_leg2.Sequence, password, _ipMarker, name, x, y, z, map));
            }
        }

        public void SendQueryList(string password)
        {
            if (_leg1.IsReady)
            {
                _leg1.Channel.Send(UOAMProtocol.BuildQueryListRequest(++_leg1.Sequence, password, _ipMarker));
            }

            if (_leg2.IsReady)
            {
                _leg2.Channel.Send(UOAMProtocol.BuildQueryListRequest(++_leg2.Sequence, password, _ipMarker));
            }
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

        private void OnLegFrame(Leg leg, byte[] data)
        {
            switch (UOAMProtocol.Classify(data))
            {
                case UOAMResponseKind.HandshakeAck:
                    bool wasConnected = IsConnected;

                    if (UOAMProtocol.TryParseHandshakeAck(data, out uint clientId))
                    {
                        leg.HandshakeClientId = clientId;
                    }

                    // Real clients always send this immediately after the handshake ack,
                    // before any position/list traffic - see UOAMProtocol's
                    // MysteryPacketTemplate comment.
                    leg.Channel.Send(UOAMProtocol.BuildMysteryFollowup(++leg.Sequence, leg.HandshakeClientId));

                    leg.HandshakeAcked = true;

                    if (!wasConnected)
                    {
                        Connected?.Invoke();
                    }

                    // Chat only works over its own dedicated, separately-handshaken
                    // connection - open it once, bound to whichever leg acked first.
                    if (!_chatOpened)
                    {
                        _chatOpened = true;
                        _chat.Connect(_connectIp, _connectPort);
                    }
                    break;

                case UOAMResponseKind.PlayerList:
                    var players = new List<UOAMPlayerInfo>();
                    UOAMProtocol.TryParsePlayerList(data, players);
                    PlayerListReceived?.Invoke(players);
                    break;

                case UOAMResponseKind.Fault:
                    // Server considers this leg dead (see UOAMProtocol.Classify) - it
                    // will follow up with a FIN/RST shortly if it hasn't already.
                    // Disconnect proactively instead of waiting for a subsequent Send()
                    // to throw, so the other leg (or a redial - see OnLegDisconnected)
                    // takes over sooner.
                    Log.Warn($"UOAM[{(leg == _leg1 ? "primary" : "primary2")}] connection faulted (status 0x{ReadFaultStatus(data):x8})");
                    leg.Channel.Fault();
                    break;

                case UOAMResponseKind.Ack:
                case UOAMResponseKind.NoChat:
                case UOAMResponseKind.ChatData:
                case UOAMResponseKind.Unknown:
                    break; // nothing to do (chat-shaped responses never arrive on a position/list leg)
            }
        }

        private static uint ReadFaultStatus(byte[] data) =>
            data.Length >= 28 ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24, 4)) : 0;

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

                case UOAMResponseKind.Fault:
                    // Chat faulting doesn't need to tear down position/list, which keep
                    // working on either leg regardless.
                    Log.Warn($"UOAM: chat connection faulted (status 0x{ReadFaultStatus(data):x8})");
                    _chat.Fault();
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
