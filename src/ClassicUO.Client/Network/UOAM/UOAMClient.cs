// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ClassicUO.Network.Socket;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network.UOAM
{
    internal sealed class UOAMClient
    {
        private const int ReadScratchSize = 4096;
        private const int RecvBufferSize = 16384;

        private const long ConnectTimeoutMs = 8000;

        private readonly byte[] _readScratch = new byte[ReadScratchSize];
        private readonly byte[] _recvBuffer = new byte[RecvBufferSize];
        private readonly byte[] _ipMarker = new byte[4];
        private int _recvLength;
        private SocketWrapper _socket;
        private uint _sequence;
        private uint _handshakeClientId;
        private bool _handshakeAcked;

        // TcpSocketWrapper.Connect() blocks synchronously with no timeout (can hang far
        // longer than any reasonable UI wait, e.g. an unreachable/black-holed address).
        // Called from a UI button handler that would otherwise freeze the whole game
        // loop, so the actual connect attempt runs on a background thread and this
        // client only observes it (via these volatile flags) from Update(), which
        // always runs on the main thread. `_connectGeneration` guards against a stale
        // background attempt (abandoned via timeout or a fresh Connect() call) still
        // writing results after a newer attempt has already started.
        private volatile bool _connecting;
        private volatile bool _pendingConnectSuccess;
        private volatile bool _pendingConnectFailure;
        private int _connectGeneration;
        private long _connectDeadline;

        public bool IsConnected => _handshakeAcked && _socket != null && _socket.IsConnected;
        public bool IsConnecting => _connecting;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<List<UOAMPlayerInfo>> PlayerListReceived;
        public event Action<string, string> ChatTextReceived;

        public void Connect(string ip, ushort port)
        {
            Disconnect();

            _handshakeAcked = false;
            _handshakeClientId = 0;
            _recvLength = 0;
            _sequence = 0;
            _pendingConnectSuccess = false;
            _pendingConnectFailure = false;
            _connecting = true;
            _connectDeadline = Environment.TickCount64 + ConnectTimeoutMs;
            Array.Clear(_ipMarker, 0, _ipMarker.Length);

            int generation = ++_connectGeneration;

            SocketWrapper socket = new TcpSocketWrapper();
            socket.OnConnected += (_, _) => { if (generation == _connectGeneration) _pendingConnectSuccess = true; };
            socket.OnDisconnected += (_, _) => { if (generation == _connectGeneration) _pendingConnectFailure = true; };
            socket.OnError += (_, e) =>
            {
                Log.Warn($"UOAM socket error: {e}");
                if (generation == _connectGeneration)
                {
                    _pendingConnectFailure = true;
                }
            };

            _socket = socket;

            Task.Run(() =>
            {
                try
                {
                    socket.Connect(new Uri($"tcp://{ip}:{port}"));
                }
                catch (Exception ex)
                {
                    Log.Warn($"UOAM connect failed: {ex}");

                    if (generation == _connectGeneration)
                    {
                        _pendingConnectFailure = true;
                    }
                }
            });
        }

        public void Disconnect()
        {
            _connecting = false;
            ++_connectGeneration; // orphan any in-flight background connect attempt

            if (_socket == null)
            {
                return;
            }

            _socket.Disconnect();
            _socket = null;
            _handshakeAcked = false;
        }

        private void HandleDisconnected()
        {
            _connecting = false;
            _socket = null;
            _handshakeAcked = false;
            Disconnected?.Invoke();
        }

        private void SendHandshake()
        {
            Send(UOAMProtocol.BuildHandshake(++_sequence));
        }

        private void UpdateLocalIpMarker()
        {
            try
            {
                if (_socket?.LocalEndPoint is IPEndPoint ep)
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

            Send(UOAMProtocol.BuildPositionUpdate(++_sequence, password, _ipMarker, name, x, y, z, map));
        }

        public void SendQueryList(string password)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildQueryListRequest(++_sequence, password, _ipMarker));
        }

        public void SendChatPoll(string password, string playerName)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildChatPollRequest(++_sequence, password, _ipMarker, playerName));
        }

        public void SendChatText(string password, string playerName, string text)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildChatSendRequest(++_sequence, password, _ipMarker, playerName, text));
        }

        private void Send(byte[] packet)
        {
            try
            {
                _socket?.Send(packet, 0, packet.Length);
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
            {
                // NetworkStream.Write wraps socket failures in IOException (not
                // SocketException) - an unhandled one here previously crashed the whole
                // client, since this runs on the main game loop thread with nothing
                // above it to catch a stray exception.
                Log.Warn($"UOAM send failed: {ex}");
                HandleDisconnected();
            }
        }

        public void Update()
        {
            if (_connecting)
            {
                if (_pendingConnectSuccess)
                {
                    _pendingConnectSuccess = false;
                    UpdateLocalIpMarker();
                    _connectDeadline = Environment.TickCount64 + ConnectTimeoutMs; // give the handshake ack its own window
                    SendHandshake();
                }
                else if (_pendingConnectFailure)
                {
                    _connecting = false;
                    _pendingConnectFailure = false;
                    HandleDisconnected();
                }
                else if (Environment.TickCount64 >= _connectDeadline)
                {
                    Log.Warn("UOAM connect timed out");
                    ++_connectGeneration; // orphan the stuck background attempt, if any
                    _connecting = false;
                    HandleDisconnected();
                }
            }

            if (_socket == null || !_socket.IsConnected)
            {
                return;
            }

            int read;

            try
            {
                do
                {
                    read = _socket.Read(_readScratch);

                    if (read > 0)
                    {
                        AppendToRecvBuffer(_readScratch, read);
                    }
                }
                while (read == _readScratch.Length);
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
            {
                // NetworkStream.Read wraps socket failures in IOException (not
                // SocketException) - same crash risk as Send(), see its comment.
                Log.Warn($"UOAM read failed: {ex}");
                HandleDisconnected();
                return;
            }

            ProcessBufferedPackets();
        }

        private void AppendToRecvBuffer(byte[] data, int length)
        {
            if (_recvLength + length > _recvBuffer.Length)
            {
                Log.Warn("UOAM recv buffer overflow, dropping connection");
                Disconnect();
                return;
            }

            Buffer.BlockCopy(data, 0, _recvBuffer, _recvLength, length);
            _recvLength += length;
        }

        private void ProcessBufferedPackets()
        {
            int position = 0;

            while (_recvLength - position >= UOAMProtocol.HeaderSize)
            {
                ReadOnlySpan<byte> remaining = _recvBuffer.AsSpan(position, _recvLength - position);

                if (!UOAMProtocol.TryReadFrameLength(remaining, out uint length) || length < 16 || length > remaining.Length)
                {
                    break; // wait for more data (or malformed - stop processing this batch)
                }

                DispatchPacket(remaining.Slice(0, (int)length));
                position += (int)length;
            }

            if (position > 0)
            {
                int leftover = _recvLength - position;
                Buffer.BlockCopy(_recvBuffer, position, _recvBuffer, 0, leftover);
                _recvLength = leftover;
            }
        }

        private void DispatchPacket(ReadOnlySpan<byte> data)
        {
            switch (UOAMProtocol.Classify(data))
            {
                case UOAMResponseKind.HandshakeAck:
                    _connecting = false;

                    if (UOAMProtocol.TryParseHandshakeAck(data, out uint clientId))
                    {
                        _handshakeClientId = clientId;
                    }

                    // Real clients always send this immediately after the handshake ack,
                    // before any position/list/chat traffic - see UOAMProtocol's
                    // MysteryPacketTemplate comment. A fresh random "activity" GUID per
                    // connection, matching the high-entropy bytes seen in real captures.
                    Span<byte> activityGuid = stackalloc byte[16];
                    Guid.NewGuid().TryWriteBytes(activityGuid);
                    Send(UOAMProtocol.BuildMysteryFollowup(++_sequence, _handshakeClientId, activityGuid));

                    _handshakeAcked = true;
                    Connected?.Invoke();
                    break;

                case UOAMResponseKind.PlayerList:
                    var players = new List<UOAMPlayerInfo>();
                    UOAMProtocol.TryParsePlayerList(data, players);
                    PlayerListReceived?.Invoke(players);
                    break;

                case UOAMResponseKind.ChatData:
                    if (UOAMProtocol.TryParseChatData(data, out byte subtype, out string senderName, out string text) && subtype == 0x01 && text != null)
                    {
                        ChatTextReceived?.Invoke(senderName, text);
                    }
                    break;

                case UOAMResponseKind.Ack:
                case UOAMResponseKind.NoChat:
                case UOAMResponseKind.Unknown:
                    break; // nothing to do
            }
        }
    }
}
