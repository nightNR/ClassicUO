// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using ClassicUO.Network.Socket;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network.UOAM
{
    internal sealed class UOAMClient
    {
        private const int ReadScratchSize = 4096;
        private const int RecvBufferSize = 16384;

        private readonly byte[] _readScratch = new byte[ReadScratchSize];
        private readonly byte[] _recvBuffer = new byte[RecvBufferSize];
        private int _recvLength;
        private SocketWrapper _socket;
        private uint _sequence;
        private bool _handshakeAcked;

        public bool IsConnected => _handshakeAcked && _socket != null && _socket.IsConnected;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<List<UOAMPlayerInfo>> PlayerListReceived;
        public event Action<string> ChatTextReceived;

        public void Connect(string ip, ushort port)
        {
            Disconnect();

            _handshakeAcked = false;
            _recvLength = 0;
            _sequence = 0;

            _socket = new TcpSocketWrapper();
            _socket.OnConnected += (_, _) => SendHandshake();
            _socket.OnDisconnected += (_, _) => HandleDisconnected();
            _socket.OnError += (_, e) =>
            {
                Log.Warn($"UOAM socket error: {e}");
                HandleDisconnected();
            };

            _socket.Connect(new Uri($"tcp://{ip}:{port}"));
        }

        public void Disconnect()
        {
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
            _socket = null;
            _handshakeAcked = false;
            Disconnected?.Invoke();
        }

        private void SendHandshake()
        {
            Send(UOAMProtocol.BuildHandshake(++_sequence));
        }

        public void SendPositionUpdate(string password, string name, int x, int y, int z, int map)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildPositionUpdate(++_sequence, password, name, x, y, z, map));
        }

        public void SendQueryList(string password)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildQueryListRequest(++_sequence, password));
        }

        public void SendChatPoll(string password)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildChatPollRequest(++_sequence, password));
        }

        public void SendChatText(string password, string text)
        {
            if (!IsConnected)
            {
                return;
            }

            Send(UOAMProtocol.BuildChatSendRequest(++_sequence, password, text));
        }

        private void Send(byte[] packet)
        {
            try
            {
                _socket?.Send(packet, 0, packet.Length);
            }
            catch (SocketException ex)
            {
                Log.Warn($"UOAM send failed: {ex}");
                HandleDisconnected();
            }
        }

        public void Update()
        {
            if (_socket == null || !_socket.IsConnected)
            {
                return;
            }

            int read;

            do
            {
                read = _socket.Read(_readScratch);

                if (read > 0)
                {
                    AppendToRecvBuffer(_readScratch, read);
                }
            }
            while (read == _readScratch.Length);

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
                    _handshakeAcked = true;
                    Connected?.Invoke();
                    break;

                case UOAMResponseKind.PlayerList:
                    var players = new List<UOAMPlayerInfo>();
                    UOAMProtocol.TryParsePlayerList(data, players);
                    PlayerListReceived?.Invoke(players);
                    break;

                case UOAMResponseKind.ChatData:
                    if (UOAMProtocol.TryParseChatData(data, out byte subtype, out string text) && subtype == 0x01 && text != null)
                    {
                        ChatTextReceived?.Invoke(text);
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
