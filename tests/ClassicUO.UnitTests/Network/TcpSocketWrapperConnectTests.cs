// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ClassicUO.Network.Socket;
using Xunit;

namespace ClassicUO.UnitTests.Network
{
    public class TcpSocketWrapperConnectTests
    {
        [Fact]
        public void OrderForConnect_PutsIPv4First_KeepingDnsOrderWithinFamily()
        {
            var v6a = IPAddress.Parse("2a01:430:45::119");
            var v4a = IPAddress.Parse("178.238.38.119");
            var v6b = IPAddress.Parse("::1");
            var v4b = IPAddress.Parse("10.0.0.1");

            IPAddress[] ordered = TcpSocketWrapper.OrderForConnect(new[] { v6a, v4a, v6b, v4b });

            Assert.Equal(new[] { v4a, v4b, v6a, v6b }, ordered);
        }

        [Fact]
        public void ConnectFirstReachable_SkipsUnreachableAddress_AndConnectsToNext()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint) listener.LocalEndpoint).Port;
                // TEST-NET-1 (RFC 5737): never routed, so the attempt either fails fast or times out.
                var unreachable = IPAddress.Parse("192.0.2.1");

                var sw = Stopwatch.StartNew();
                using TcpClient client = TcpSocketWrapper.ConnectFirstReachable(
                    new[] { unreachable, IPAddress.Loopback }, port, TimeSpan.FromMilliseconds(500));

                Assert.True(client.Connected);
                Assert.Equal(IPAddress.Loopback, ((IPEndPoint) client.Client.RemoteEndPoint).Address);
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void ConnectFirstReachable_ThrowsWhenNoAddressConnects()
        {
            // Bind then release a port so nothing listens on it: loopback refuses immediately.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint) probe.LocalEndpoint).Port;
            probe.Stop();

            Assert.Throws<SocketException>(() =>
                TcpSocketWrapper.ConnectFirstReachable(new[] { IPAddress.Loopback }, port, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void ConnectFirstReachable_ThrowsHostNotFound_ForNoAddresses()
        {
            var ex = Assert.Throws<SocketException>(() =>
                TcpSocketWrapper.ConnectFirstReachable(Array.Empty<IPAddress>(), 2593, TimeSpan.FromSeconds(1)));

            Assert.Equal(SocketError.HostNotFound, ex.SocketErrorCode);
        }
    }
}
