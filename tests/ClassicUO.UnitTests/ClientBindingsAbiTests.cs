// SPDX-License-Identifier: BSD-2-Clause

using System.Reflection;
using System.Runtime.InteropServices;
using ClassicUO.PluginAbi;
using Xunit;

namespace ClassicUO.UnitTests
{
    /// <summary>
    /// Locks in the append-only ABI contract for <see cref="ClassicUO.PluginAbi.ClientBindings"/>.
    /// This struct's raw memory layout is read by pre-compiled external v1 plugin
    /// binaries (e.g. external/cuoapi/cuoapi.dll) that were built against a specific,
    /// frozen field order. Inserting a new field ANYWHERE but the end shifts every
    /// field after it, silently corrupting those binaries' reads — a crash inside
    /// whatever function pointer field lands wherever they expect, with no compiler
    /// error. These tests fail loudly if that ever happens again.
    /// </summary>
    public unsafe class ClientBindingsAbiTests
    {
        [Fact]
        public void PreExistingFields_KeepTheirOriginalByteOffsets()
        {
            int ptrSize = sizeof(nint);

            // Field index (0-based) as declared before the IHighlight feature ever
            // touched this struct. Each new feature must only ever APPEND fields —
            // never insert — so these offsets must never change.
            Assert.Equal(0 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("PluginRecvFn"));
            Assert.Equal(8 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("ReflectionCmdFn"));
            Assert.Equal(21 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("ClearTimersFn"));
            Assert.Equal(22 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("SetPluginPartyMemberFn"));
            Assert.Equal(25 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("CheckLosFn"));
            Assert.Equal(26 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("CheckLosBatchFn"));
        }

        [Fact]
        public void ClientVersion_IsStillTheLastField()
        {
            int clientVersionOffset = (int)Marshal.OffsetOf<ClientBindings>("ClientVersion");

            foreach (var field in typeof(ClientBindings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == "ClientVersion")
                {
                    continue;
                }

                int otherOffset = (int)Marshal.OffsetOf<ClientBindings>(field.Name);
                Assert.True(
                    otherOffset < clientVersionOffset,
                    $"{field.Name} (offset {otherOffset}) must come before ClientVersion (offset {clientVersionOffset})."
                );
            }
        }

        [Fact]
        public void HighlightFields_AreAppendedAfterCheckLosBatchFn_BeforeClientVersion()
        {
            int ptrSize = sizeof(nint);
            int checkLosBatchOffset = (int)Marshal.OffsetOf<ClientBindings>("CheckLosBatchFn");
            int clientVersionOffset = (int)Marshal.OffsetOf<ClientBindings>("ClientVersion");

            Assert.Equal(checkLosBatchOffset + ptrSize, (int)Marshal.OffsetOf<ClientBindings>("AddAreaFn"));

            // SetStatusBarPriorityFn was appended after the 7 highlight fields and
            // before ClientVersion (still last), per the append-only ABI rule.
            Assert.Equal(checkLosBatchOffset + 8 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("SetStatusBarPriorityFn"));
            // GenericInvokeFn (generic dispatch) was appended after that, still
            // before ClientVersion — see GenericInvokeFn_IsAppendedRightBeforeClientVersion.
            Assert.Equal(checkLosBatchOffset + 9 * ptrSize, (int)Marshal.OffsetOf<ClientBindings>("GenericInvokeFn"));
            Assert.Equal(checkLosBatchOffset + 10 * ptrSize, clientVersionOffset);
        }

        [Fact]
        public void GenericInvokeFn_IsAppendedRightBeforeClientVersion()
        {
            int ptrSize = sizeof(nint);
            int genericInvokeOffset = (int)Marshal.OffsetOf<ClientBindings>("GenericInvokeFn");
            int clientVersionOffset = (int)Marshal.OffsetOf<ClientBindings>("ClientVersion");

            Assert.Equal(genericInvokeOffset + ptrSize, clientVersionOffset);
        }

        [Fact]
        public void GenericEventFn_IsTheLastHostBindingsField()
        {
            int ptrSize = sizeof(nint);
            int genericEventOffset = (int)Marshal.OffsetOf<HostBindings>("GenericEventFn");

            Assert.Equal(Marshal.SizeOf<HostBindings>() - ptrSize, genericEventOffset);
        }
    }
}
