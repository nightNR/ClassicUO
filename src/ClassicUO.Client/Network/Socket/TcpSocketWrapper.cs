using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network.Socket;

sealed class TcpSocketWrapper : SocketWrapper
{
    private TcpClient _socket;

    public override bool IsConnected => _socket?.Client?.Connected ?? false;

    public override EndPoint LocalEndPoint => _socket?.Client?.LocalEndPoint;


    // Budget for every address but the last. Connect runs on the game thread before the first
    // frame (auto-login), and the OS gives up on a silently-dropped SYN only after ~21 s.
    internal static readonly TimeSpan PerAddressTimeout = TimeSpan.FromSeconds(5);

    public override void Connect(Uri uri)
    {
        if (IsConnected)
            return;

        try
        {
            // Resolved on every connect, so a server that moved to a new IP is picked up from DNS.
            IPAddress[] addresses = IPAddress.TryParse(uri.Host, out IPAddress literal)
                ? new[] { literal }
                : Dns.GetHostAddresses(uri.Host);

            _socket = ConnectFirstReachable(OrderForConnect(addresses), uri.Port, PerAddressTimeout);

            if (!IsConnected)
            {
                InvokeOnError(SocketError.NotConnected);

                return;
            }

            InvokeOnConnected();
        }
        catch (SocketException socketEx)
        {
            Log.Error($"error while connecting {socketEx}");
            InvokeOnError(socketEx.SocketErrorCode);
        }
        catch (Exception ex)
        {
            Log.Error($"error while connecting {ex}");
            InvokeOnError(SocketError.SocketError);
        }
    }

    /// <summary>
    /// IPv4 first, then IPv6, each keeping DNS order. TcpClient.Connect(host, port) follows the OS
    /// preference (IPv6 first), so a hostname with an AAAA record whose IPv6 path is dead (server
    /// not listening on v6, broken home IPv6) froze the client ~21 s on every connect before it
    /// fell back to IPv4. Shard servers are overwhelmingly IPv4-only; IPv6 stays as the fallback.
    /// </summary>
    internal static IPAddress[] OrderForConnect(IPAddress[] addresses)
    {
        var ordered = new List<IPAddress>(addresses.Length);
        ordered.AddRange(addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork));
        ordered.AddRange(addresses.Where(a => a.AddressFamily != AddressFamily.InterNetwork));
        return ordered.ToArray();
    }

    /// <summary>
    /// Tries each address in turn; all but the last are cut off after <paramref name="perAddressTimeout"/>
    /// so one unreachable address cannot stall the connect. The last one gets the OS timeout.
    /// Throws the last failure when no address connects.
    /// </summary>
    internal static TcpClient ConnectFirstReachable(IReadOnlyList<IPAddress> addresses, int port, TimeSpan perAddressTimeout)
    {
        if (addresses.Count == 0)
            throw new SocketException((int) SocketError.HostNotFound);

        Exception lastError = null;

        for (int i = 0; i < addresses.Count; i++)
        {
            IPAddress address = addresses[i];
            bool isLast = i == addresses.Count - 1;
            var client = new TcpClient(address.AddressFamily) { NoDelay = true };

            try
            {
                using var cts = isLast ? new CancellationTokenSource() : new CancellationTokenSource(perAddressTimeout);
                client.ConnectAsync(address, port, cts.Token).AsTask().GetAwaiter().GetResult();

                return client;
            }
            catch (OperationCanceledException)
            {
                lastError = new SocketException((int) SocketError.TimedOut);
                Log.Warn($"connect to {address}:{port} timed out after {perAddressTimeout.TotalSeconds:0}s, trying next address");
            }
            catch (SocketException ex)
            {
                lastError = ex;
                Log.Warn($"connect to {address}:{port} failed ({ex.SocketErrorCode}){(isLast ? "" : ", trying next address")}");
            }

            client.Dispose();
        }

        throw lastError;
    }

    public override void Send(byte[] buffer, int offset, int count)
    {
        var stream = _socket.GetStream();
        stream.Write(buffer, offset, count);
        stream.Flush();
    }

    public override int Read(byte[] buffer)
    {
        if (!IsConnected)
            return 0;

        var available = Math.Min(buffer.Length, _socket.Available);
        var done = 0;

        var stream = _socket.GetStream();

        while (done < available)
        {
            var toRead = Math.Min(buffer.Length, available - done);
            var read = stream.Read(buffer, done, toRead);

            if (read <= 0)
            {
                InvokeOnDisconnected();
                Disconnect();

                return 0;
            }

            done += read;
        }

        return done;
    }

    public override void Disconnect()
    {
        _socket?.Close();
        Dispose();
    }

    public override void Dispose()
    {
        _socket?.Dispose();
    }
}