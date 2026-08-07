// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using ClassicUO.Network.Socket;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network.UOAM
{
    // A single UOAM TCP connection: async connect-with-timeout, non-blocking read pump,
    // and length-prefixed frame extraction. UOAMClient owns two of these - one for the
    // primary connection (position/list) and one for the dedicated chat connection (see
    // docs/uoam-protocol.md) - since a real client always uses two separate sockets and
    // chat never works on the primary one alone. Everything UOAM-protocol-specific
    // (handshakes, sequence numbers, packet classification) lives in UOAMClient; this
    // class only knows about raw framed bytes.
    internal sealed class UOAMSocketChannel
    {
        private const int ReadScratchSize = 4096;
        private const int RecvBufferSize = 16384;
        private const long ConnectTimeoutMs = 8000;

        private readonly byte[] _readScratch = new byte[ReadScratchSize];
        private readonly byte[] _recvBuffer = new byte[RecvBufferSize];
        private int _recvLength;
        private SocketWrapper _socket;

        // TcpSocketWrapper.Connect() blocks synchronously with no timeout, so the actual
        // connect attempt runs on a background thread and this channel only observes it
        // (via these volatile flags) from Update(), which always runs on the main
        // thread. `_connectGeneration` guards against a stale background attempt
        // (abandoned via timeout or a fresh Connect() call) still writing results after
        // a newer attempt has already started.
        private volatile bool _connecting;
        private volatile bool _pendingConnectSuccess;
        private volatile bool _pendingConnectFailure;
        private int _connectGeneration;
        private long _connectDeadline;
        private readonly string _name;

        public bool IsConnected => _socket != null && _socket.IsConnected;
        public bool IsConnecting => _connecting;
        public System.Net.EndPoint LocalEndPoint => _socket?.LocalEndPoint;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> FrameReceived;

        public UOAMSocketChannel(string name)
        {
            _name = name;
        }

        public void Connect(string ip, ushort port)
        {
            Disconnect();

            _recvLength = 0;
            _pendingConnectSuccess = false;
            _pendingConnectFailure = false;
            _connecting = true;
            _connectDeadline = Environment.TickCount64 + ConnectTimeoutMs;

            int generation = ++_connectGeneration;

            // Abortive close (RST) - see TcpSocketWrapper's constructor comment. A real
            // UOAM client always RSTs on disconnect; matching that gets us evicted from
            // the hub's player list as promptly as it is, instead of lingering after a
            // graceful FIN.
            SocketWrapper socket = new TcpSocketWrapper(abortiveClose: true);
            socket.OnConnected += (_, _) => { if (generation == _connectGeneration) _pendingConnectSuccess = true; };
            socket.OnDisconnected += (_, _) => { if (generation == _connectGeneration) _pendingConnectFailure = true; };
            socket.OnError += (_, e) =>
            {
                Log.Warn($"UOAM[{_name}] socket error: {e}");
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
                    Log.Warn($"UOAM[{_name}] connect failed: {ex}");

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
        }

        // The protocol layer decided this connection is dead (e.g. a DCE/RPC fault
        // response - see UOAMProtocol.Classify) even though the socket itself hasn't
        // failed yet. Close it and fire Disconnected, same as a genuine socket failure
        // would, so callers don't need two different "this connection is over" paths.
        public void Fault()
        {
            _connecting = false;
            ++_connectGeneration;
            _socket?.Disconnect();
            HandleDisconnected();
        }

        public void ExtendConnectDeadline()
        {
            _connectDeadline = Environment.TickCount64 + ConnectTimeoutMs;
        }

        public void Send(byte[] packet)
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
                Log.Warn($"UOAM[{_name}] send failed: {ex}");
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
                    _connecting = false;
                    Connected?.Invoke();
                }
                else if (_pendingConnectFailure)
                {
                    _connecting = false;
                    _pendingConnectFailure = false;
                    HandleDisconnected();
                }
                else if (Environment.TickCount64 >= _connectDeadline)
                {
                    Log.Warn($"UOAM[{_name}] connect timed out");
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
                Log.Warn($"UOAM[{_name}] read failed: {ex}");
                HandleDisconnected();
                return;
            }

            ProcessBufferedPackets();
        }

        private void HandleDisconnected()
        {
            _socket = null;
            Disconnected?.Invoke();
        }

        private void AppendToRecvBuffer(byte[] data, int length)
        {
            if (_recvLength + length > _recvBuffer.Length)
            {
                Log.Warn($"UOAM[{_name}] recv buffer overflow, dropping connection");
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

                FrameReceived?.Invoke(remaining.Slice(0, (int)length).ToArray());
                position += (int)length;
            }

            if (position > 0)
            {
                int leftover = _recvLength - position;
                Buffer.BlockCopy(_recvBuffer, position, _recvBuffer, 0, leftover);
                _recvLength = leftover;
            }
        }
    }
}
