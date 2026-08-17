// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Managers;
using ClassicUO.PluginAbi;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    [Collection("PluginManagerState")]
    public class PluginStatusBarEventsTests
    {
        public PluginStatusBarEventsTests()
        {
            PluginStatusBarEvents.EventSink = null;
        }

        [Fact]
        public void RaiseOpened_SendsSerialAsFourBytePayload()
        {
            var events = new List<(int eventId, byte[] payload)>();
            PluginStatusBarEvents.EventSink = (id, payload) => events.Add((id, payload));

            PluginStatusBarEvents.RaiseOpened(0x1234u);

            Assert.Single(events);
            Assert.Equal(GenericEventIds.StatusBarOpened, events[0].eventId);
            Assert.Equal(0x1234u, System.BitConverter.ToUInt32(events[0].payload, 0));
        }

        [Fact]
        public void RaiseClosed_SendsSerialAsFourBytePayload()
        {
            var events = new List<(int eventId, byte[] payload)>();
            PluginStatusBarEvents.EventSink = (id, payload) => events.Add((id, payload));

            PluginStatusBarEvents.RaiseClosed(0x5678u);

            Assert.Single(events);
            Assert.Equal(GenericEventIds.StatusBarClosed, events[0].eventId);
            Assert.Equal(0x5678u, System.BitConverter.ToUInt32(events[0].payload, 0));
        }

        [Fact]
        public unsafe void RaiseChanged_RoundTripsThePayloadStruct()
        {
            var events = new List<(int eventId, byte[] payload)>();
            PluginStatusBarEvents.EventSink = (id, payload) => events.Add((id, payload));

            var payload = new StatusBarChangedPayload
            {
                Serial = 0xAAAA,
                HpPercent = 80,
                ManaPercent = 50,
                StaminaPercent = 30,
                Flags = (byte)(StatusBarFlags.Poisoned | StatusBarFlags.WarMode),
                Active = 1,
                GroupId = 7,
                X = 100,
                Y = 200,
            };

            PluginStatusBarEvents.RaiseChanged(payload);

            Assert.Single(events);
            Assert.Equal(GenericEventIds.StatusBarChanged, events[0].eventId);

            fixed (byte* p = events[0].payload)
            {
                var roundTripped = *(StatusBarChangedPayload*)p;
                Assert.Equal(payload.Serial, roundTripped.Serial);
                Assert.Equal(payload.HpPercent, roundTripped.HpPercent);
                Assert.Equal(payload.Flags, roundTripped.Flags);
                Assert.Equal(payload.GroupId, roundTripped.GroupId);
                Assert.Equal(payload.X, roundTripped.X);
                Assert.Equal(payload.Y, roundTripped.Y);
            }
        }

        [Fact]
        public void ListOpen_WithNoOpenBars_ReturnsZeroBytesWritten()
        {
            var written = PluginStatusBarEvents.ListOpen(System.IntPtr.Zero, 0);
            Assert.Equal(0, written);
        }
    }
}
