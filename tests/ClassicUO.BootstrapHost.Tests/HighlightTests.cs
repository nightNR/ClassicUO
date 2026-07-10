// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies each IHighlight method dispatches into the matching ClientBindings
/// function pointer with the right argument marshaling. A fake binding records
/// the call; no cuo.dll is involved.
/// </summary>
public sealed unsafe class HighlightTests
{
    private static string _lastId;
    private static int _durationMs, _snapKind, _x, _y, _rangeX, _rangeY, _objectTypes;
    private static uint _anchorSerial, _serial;
    private static ushort _hue;
    private static byte _priority;
    private static int _addAreaCalls, _removeAreaCalls, _clearAreasCalls, _getTimerCalls;
    private static int _addCharCalls, _removeCharCalls, _clearCharCalls;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureAddArea(nint idPtr, int durationMs, int snapKind, uint anchorSerial, int x, int y, ushort hue, int rangeX, int rangeY, int objectTypes)
    {
        _lastId = Marshal.PtrToStringAnsi(idPtr);
        _durationMs = durationMs; _snapKind = snapKind; _anchorSerial = anchorSerial;
        _x = x; _y = y; _hue = hue; _rangeX = rangeX; _rangeY = rangeY; _objectTypes = objectTypes;
        _addAreaCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureRemoveArea(nint idPtr) { _lastId = Marshal.PtrToStringAnsi(idPtr); _removeAreaCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureClearAreas() { _clearAreasCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CaptureGetAreaTimer(nint idPtr) { _lastId = Marshal.PtrToStringAnsi(idPtr); _getTimerCalls++; return 4242; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureAddCharacter(uint serial, ushort hue, byte priority)
    { _serial = serial; _hue = hue; _priority = priority; _addCharCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureRemoveCharacter(uint serial, byte priority) { _serial = serial; _priority = priority; _removeCharCalls++; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CaptureClearCharacters(byte priority) { _priority = priority; _clearCharCalls++; }

    private static HighlightImpl NewImplWithBindings(ClientBindings bindings)
    {
        var bridge = new HostBridge();
        bridge.SetClientBindingsForTest(bindings);
        return new HighlightImpl(bridge);
    }

    [Fact]
    public void AddArea_InvokesBinding_WithArgs()
    {
        _addAreaCalls = 0;
        var bindings = new ClientBindings
        {
            AddAreaFn = (nint)(delegate* unmanaged[Cdecl]<nint, int, int, uint, int, int, ushort, int, int, int, void>)&CaptureAddArea
        };
        var impl = NewImplWithBindings(bindings);

        impl.AddArea("zone1", durationMs: 5000, snap: ClassicUO.PluginApi.HighlightSnap.Position,
            anchorSerial: 0, hue: 0x0021, rangeX: 4, rangeY: 4,
            objectTypes: ClassicUO.PluginApi.HighlightObjectTypes.Land, x: 10, y: 20);

        _addAreaCalls.Should().Be(1);
        _lastId.Should().Be("zone1");
        _durationMs.Should().Be(5000);
        _snapKind.Should().Be((int)ClassicUO.PluginApi.HighlightSnap.Position);
        _hue.Should().Be((ushort)0x0021);
        _rangeX.Should().Be(4);
        _rangeY.Should().Be(4);
        _objectTypes.Should().Be((int)ClassicUO.PluginApi.HighlightObjectTypes.Land);
        _x.Should().Be(10);
        _y.Should().Be(20);
    }

    [Fact]
    public void RemoveArea_InvokesBinding_WithId()
    {
        _removeAreaCalls = 0;
        var bindings = new ClientBindings { RemoveAreaFn = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CaptureRemoveArea };
        var impl = NewImplWithBindings(bindings);

        impl.RemoveArea("zone1");

        _removeAreaCalls.Should().Be(1);
        _lastId.Should().Be("zone1");
    }

    [Fact]
    public void ClearAreas_InvokesBinding()
    {
        _clearAreasCalls = 0;
        var bindings = new ClientBindings { ClearAreasFn = (nint)(delegate* unmanaged[Cdecl]<void>)&CaptureClearAreas };
        var impl = NewImplWithBindings(bindings);

        impl.ClearAreas();

        _clearAreasCalls.Should().Be(1);
    }

    [Fact]
    public void GetAreaTimer_ReturnsBindingResult()
    {
        _getTimerCalls = 0;
        var bindings = new ClientBindings { GetAreaTimerFn = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&CaptureGetAreaTimer };
        var impl = NewImplWithBindings(bindings);

        int result = impl.GetAreaTimer("zone1");

        _getTimerCalls.Should().Be(1);
        result.Should().Be(4242);
    }

    [Fact]
    public void AddCharacter_InvokesBinding_WithPriorityByte()
    {
        _addCharCalls = 0;
        var bindings = new ClientBindings { AddCharacterFn = (nint)(delegate* unmanaged[Cdecl]<uint, ushort, byte, void>)&CaptureAddCharacter };
        var impl = NewImplWithBindings(bindings);

        impl.AddCharacter(0x1234, 0x0055, priorityHighlight: true);

        _addCharCalls.Should().Be(1);
        _serial.Should().Be(0x1234u);
        _hue.Should().Be((ushort)0x0055);
        _priority.Should().Be((byte)1);
    }

    [Fact]
    public void RemoveCharacter_InvokesBinding()
    {
        _removeCharCalls = 0;
        var bindings = new ClientBindings { RemoveCharacterFn = (nint)(delegate* unmanaged[Cdecl]<uint, byte, void>)&CaptureRemoveCharacter };
        var impl = NewImplWithBindings(bindings);

        impl.RemoveCharacter(0x1234, priorityHighlight: false);

        _removeCharCalls.Should().Be(1);
        _serial.Should().Be(0x1234u);
        _priority.Should().Be((byte)0);
    }

    [Fact]
    public void ClearCharacters_InvokesBinding()
    {
        _clearCharCalls = 0;
        var bindings = new ClientBindings { ClearCharactersFn = (nint)(delegate* unmanaged[Cdecl]<byte, void>)&CaptureClearCharacters };
        var impl = NewImplWithBindings(bindings);

        impl.ClearCharacters(priorityHighlight: true);

        _clearCharCalls.Should().Be(1);
        _priority.Should().Be((byte)1);
    }

    [Fact]
    public void Methods_AreNoOps_WhenBindingMissing()
    {
        var impl = NewImplWithBindings(new ClientBindings());
        // Zeroed function pointers: must not throw. GetAreaTimer returns 0.
        impl.AddArea("x");
        impl.RemoveArea("x");
        impl.ClearAreas();
        impl.GetAreaTimer("x").Should().Be(0);
        impl.AddCharacter(1, 1);
        impl.RemoveCharacter(1);
        impl.ClearCharacters();
    }
}
