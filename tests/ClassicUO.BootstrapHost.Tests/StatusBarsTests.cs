// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    private static ushort _hue;
    private static int _openCalls, _closeCalls, _overlayCalls;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureOpen(uint serial, int x, int y, byte move, int group)
    {
        _serial = serial; _x = x; _y = y; _move = move; _group = group; _openCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureClose(uint serial) { _serial = serial; _closeCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureOverlay(uint serial, ushort hue) { _serial = serial; _hue = hue; _overlayCalls++; }

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
            SetOverlayFn = (nint)(delegate* unmanaged[Cdecl]<uint, ushort, void>)&CaptureOverlay
        };
        var impl = NewImplWithBindings(bindings);

        impl.SetOverlay(0x55, 0x0021);

        _overlayCalls.Should().Be(1);
        _serial.Should().Be(0x55u);
        _hue.Should().Be((ushort)0x0021);
    }

    [Fact]
    public void Methods_AreNoOps_WhenBindingMissing()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        // Zeroed function pointers: must not throw.
        impl.OpenStatusBar(1, 0, 0);
        impl.CloseStatusBar(1);
        impl.SetOverlay(1, 1);
    }
}
