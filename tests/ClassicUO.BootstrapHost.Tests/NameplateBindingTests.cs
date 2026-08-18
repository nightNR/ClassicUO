// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ClassicUO.PluginAbi;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies that NameplatesImpl.SetOverride/ClearOverride/ClearAllOverrides
/// invoke the cuo-side GenericInvokeFn with the right opcode and payload. We
/// inject a ClientBindings table whose GenericInvokeFn points at a managed
/// cdecl recorder, then call the plugin-facing INameplates surface.
/// </summary>
[Collection("BootstrapHost")]
public sealed unsafe class NameplateBindingTests : IDisposable
{
    private static int s_opcode, s_argsLen;
    private static NameplateOverridePayload s_payload;
    private static uint s_clearedSerial;

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;

    public NameplateBindingTests()
    {
        s_opcode = s_argsLen = 0;
        s_payload = default;
        s_clearedSerial = 0;

        _tempRoot = Path.Combine(Path.GetTempPath(), "cuo-bootstraphost-tests", Guid.NewGuid().ToString("N"));
        _pluginsRoot = Path.Combine(_tempRoot, "Plugins");
        var pluginDir = Path.Combine(_pluginsRoot, "HelloPlugin");
        Directory.CreateDirectory(pluginDir);

        var fixtureDll = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HelloPlugin", "HelloPlugin.dll");
        File.Exists(fixtureDll).Should().BeTrue($"HelloPlugin fixture should be staged at {fixtureDll}");
        File.Copy(fixtureDll, Path.Combine(pluginDir, "HelloPlugin.dll"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GenericInvokeRecorder(int opcode, nint argsPtr, int argsLen, nint outBuf, int outCap)
    {
        s_opcode = opcode;
        s_argsLen = argsLen;

        if (opcode == GenericOpcodes.SetNameplateOverride && argsLen >= sizeof(NameplateOverridePayload))
            s_payload = *(NameplateOverridePayload*)argsPtr;
        else if (opcode == GenericOpcodes.ClearNameplateOverride && argsLen >= sizeof(uint))
            s_clearedSerial = *(uint*)argsPtr;

        return 0;
    }

    private static HostBridge CreateBridgeWithRecorder(string pluginsRoot)
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(pluginsRoot);

        var cb = new ClientBindings
        {
            GenericInvokeFn = (nint)(delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)&GenericInvokeRecorder,
        };
        bridge.InstallClientBindingsForTest(cb);

        return bridge;
    }

    [Fact]
    public void SetOverride_invokes_generic_dispatch_with_the_payload()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.SetOverride(0xDEADBEEF, visible: true, opacityPercent: 40);

        s_opcode.Should().Be(GenericOpcodes.SetNameplateOverride);
        s_argsLen.Should().Be(sizeof(NameplateOverridePayload));
        s_payload.Serial.Should().Be(0xDEADBEEFu);
        s_payload.Visible.Should().Be((byte)1);
        s_payload.OpacityPercent.Should().Be((byte)40);
    }

    [Fact]
    public void SetOverride_clamps_opacity_above_100()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.SetOverride(0xDEADBEEF, visible: true, opacityPercent: 255);

        s_payload.OpacityPercent.Should().Be((byte)100);
    }

    [Fact]
    public void ClearOverride_invokes_generic_dispatch_with_the_serial()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.ClearOverride(0xDEADBEEF);

        s_opcode.Should().Be(GenericOpcodes.ClearNameplateOverride);
        s_argsLen.Should().Be(sizeof(uint));
        s_clearedSerial.Should().Be(0xDEADBEEFu);
    }

    [Fact]
    public void ClearAllOverrides_invokes_generic_dispatch_with_no_args()
    {
        var bridge = CreateBridgeWithRecorder(_pluginsRoot);

        bridge.Plugins[0].Nameplates.ClearAllOverrides();

        s_opcode.Should().Be(GenericOpcodes.ClearAllNameplateOverrides);
        s_argsLen.Should().Be(0);
    }
}
