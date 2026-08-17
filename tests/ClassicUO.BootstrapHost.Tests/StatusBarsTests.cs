// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ClassicUO.PluginAbi;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies each IStatusBars method dispatches into the matching ClientBindings
/// function pointer with the right argument marshaling. A fake binding records
/// the call; no cuo.dll is involved.
/// </summary>
public sealed unsafe class StatusBarsTests
{
    private static uint _serial;
    private static int _x, _y, _group;
    private static byte _move;
    private static ushort _hue, _bgHue;
    private static int _priority;
    private static int _openCalls, _closeCalls, _overlayCalls, _priorityCalls;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureOpen(uint serial, int x, int y, byte move, int group)
    {
        _serial = serial; _x = x; _y = y; _move = move; _group = group; _openCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureClose(uint serial) { _serial = serial; _closeCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureOverlay(uint serial, ushort hue, ushort bgHue) { _serial = serial; _hue = hue; _bgHue = bgHue; _overlayCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CapturePriority(uint serial, int priority) { _serial = serial; _priority = priority; _priorityCalls++; }

    private static StatusBarsImpl NewImplWithBindings(ClientBindings bindings)
    {
        var bridge = new HostBridge();
        bridge.SetClientBindingsForTest(bindings);
        return new StatusBarsImpl(bridge);
    }

    [Fact]
    public void OpenStatusBar_InvokesBinding_WithArgs()
    {
        _openCalls = 0;
        var bindings = new ClientBindings
        {
            OpenStatusBarFn = (nint)(delegate* unmanaged[Cdecl]<uint, int, int, byte, int, void>)&CaptureOpen
        };
        var impl = NewImplWithBindings(bindings);

        impl.OpenStatusBar(0x1234, 10, 20, moveIfExists: true, groupId: 7);

        _openCalls.Should().Be(1);
        _serial.Should().Be(0x1234u);
        _x.Should().Be(10);
        _y.Should().Be(20);
        _move.Should().Be(1);
        _group.Should().Be(7);
    }

    [Fact]
    public void CloseStatusBar_InvokesBinding()
    {
        _closeCalls = 0;
        var bindings = new ClientBindings
        {
            CloseStatusBarFn = (nint)(delegate* unmanaged[Cdecl]<uint, void>)&CaptureClose
        };
        var impl = NewImplWithBindings(bindings);

        impl.CloseStatusBar(0xABCD);

        _closeCalls.Should().Be(1);
        _serial.Should().Be(0xABCDu);
    }

    [Fact]
    public void SetOverlay_InvokesBinding_WithHue()
    {
        _overlayCalls = 0;
        var bindings = new ClientBindings
        {
            SetOverlayFn = (nint)(delegate* unmanaged[Cdecl]<uint, ushort, ushort, void>)&CaptureOverlay
        };
        var impl = NewImplWithBindings(bindings);

        impl.SetOverlay(0x55, 0x0021, 0x0044);

        _overlayCalls.Should().Be(1);
        _serial.Should().Be(0x55u);
        _hue.Should().Be((ushort)0x0021);
        _bgHue.Should().Be((ushort)0x0044);
    }

    [Fact]
    public void SetStatusBarPriority_InvokesBinding_WithSerialAndPriority()
    {
        _priorityCalls = 0;
        var bindings = new ClientBindings
        {
            SetStatusBarPriorityFn = (nint)(delegate* unmanaged[Cdecl]<uint, int, void>)&CapturePriority
        };
        var impl = NewImplWithBindings(bindings);

        impl.SetStatusBarPriority(0xDEADBEEF, 7);

        _priorityCalls.Should().Be(1);
        _serial.Should().Be(0xDEADBEEFu);
        _priority.Should().Be(7);
    }

    [Fact]
    public void Methods_AreNoOps_WhenBindingMissing()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        // Zeroed function pointers: must not throw.
        impl.OpenStatusBar(1, 0, 0);
        impl.CloseStatusBar(1);
        impl.SetOverlay(1, 1);
        impl.SetStatusBarPriority(1, 1);
    }

    [Fact]
    public void RaiseGenericEvent_Opened_DecodesSerial()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        uint? received = null;
        impl.Opened += s => received = s;

        impl.RaiseGenericEvent(GenericEventIds.StatusBarOpened, BitConverter.GetBytes(0x777u));

        received.Should().Be(0x777u);
    }

    [Fact]
    public void RaiseGenericEvent_Closed_DecodesSerial()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        uint? received = null;
        impl.Closed += s => received = s;

        impl.RaiseGenericEvent(GenericEventIds.StatusBarClosed, BitConverter.GetBytes(0x999u));

        received.Should().Be(0x999u);
    }

    [Fact]
    public void RaiseGenericEvent_Changed_DecodesPayload()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        ClassicUO.PluginApi.StatusBarChangeInfo? received = null;
        impl.Changed += info => received = info;

        var payload = new StatusBarChangedPayload
        {
            Serial = 0x42,
            HpPercent = 90,
            ManaPercent = 40,
            StaminaPercent = 60,
            Flags = (byte)(StatusBarFlags.Paralyzed | StatusBarFlags.Dead),
            Active = 0,
            GroupId = 3,
            X = 5,
            Y = 6,
        };
        var bytes = new byte[Marshal.SizeOf<StatusBarChangedPayload>()];
        fixed (byte* dst = bytes)
            *(StatusBarChangedPayload*)dst = payload;

        impl.RaiseGenericEvent(GenericEventIds.StatusBarChanged, bytes);

        received.Should().NotBeNull();
        received!.Value.Serial.Should().Be(0x42u);
        received.Value.HpPercent.Should().Be((byte)90);
        received.Value.Active.Should().BeFalse();
        received.Value.GroupId.Should().Be(3);
        received.Value.Flags.Should().Be(ClassicUO.PluginApi.StatusBarFlags.Paralyzed | ClassicUO.PluginApi.StatusBarFlags.Dead);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FakeListOpen(int opcode, nint argsPtr, int argsLen, nint outBuf, int outCap)
    {
        Span<uint> serials = [0x10, 0x20, 0x30];
        var needed = serials.Length * sizeof(uint);
        if (outCap < needed) return -2;
        var dst = new Span<uint>((void*)outBuf, serials.Length);
        serials.CopyTo(dst);
        return needed;
    }

    [Fact]
    public void ListOpen_InvokesGenericOpcode_AndDecodesSerials()
    {
        var bindings = new ClientBindings
        {
            GenericInvokeFn = (nint)(delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)&FakeListOpen
        };
        var impl = NewImplWithBindings(bindings);

        var result = impl.ListOpen();

        result.Should().Equal(0x10u, 0x20u, 0x30u);
    }
}
