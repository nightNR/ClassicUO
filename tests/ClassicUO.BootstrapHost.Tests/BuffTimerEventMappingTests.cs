// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.BootstrapHost;
using ClassicUO.PluginApi;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests
{
    public class BuffTimerEventMappingTests
    {
        [Fact]
        public void BuffsImpl_ExpiredReason_FiresExpiredAndRemoved()
        {
            var impl = new BuffsImpl(new HostBridge());
            int? expired = null;
            (int id, BuffRemoveReason reason)? removed = null;
            impl.Expired += id => expired = id;
            impl.Removed += (id, r) => removed = (id, r);

            impl.RaiseEvent(7, (int)BuffRemoveReason.Expired);

            Assert.Equal(7, expired);
            Assert.Equal((7, BuffRemoveReason.Expired), removed);
        }

        [Fact]
        public void BuffsImpl_RemovedByPlugin_FiresOnlyRemoved()
        {
            var impl = new BuffsImpl(new HostBridge());
            bool expiredFired = false;
            (int id, BuffRemoveReason reason)? removed = null;
            impl.Expired += _ => expiredFired = true;
            impl.Removed += (id, r) => removed = (id, r);

            impl.RaiseEvent(7, (int)BuffRemoveReason.RemovedByPlugin);

            Assert.False(expiredFired);
            Assert.Equal((7, BuffRemoveReason.RemovedByPlugin), removed);
        }

        [Fact]
        public void ScreenTimersImpl_ExpiredReason_FiresExpiredAndRemoved()
        {
            var impl = new ScreenTimersImpl(new HostBridge());
            int? expired = null;
            (int id, TimerRemoveReason reason)? removed = null;
            impl.Expired += id => expired = id;
            impl.Removed += (id, r) => removed = (id, r);

            impl.RaiseEvent(3, (int)TimerRemoveReason.Expired);

            Assert.Equal(3, expired);
            Assert.Equal((3, TimerRemoveReason.Expired), removed);
        }
    }
}
