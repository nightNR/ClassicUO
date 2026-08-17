// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ClassicUO.PluginAbi;
using ClassicUO.PluginApi;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies the opcode-based escape valve (<see cref="IRawChannel"/>) round-trips
/// through <see cref="ClientBindings.GenericInvokeFn"/> and that cuo-pushed
/// generic events fan out to plugins via <see cref="HostBindings.GenericEventFn"/>.
/// </summary>
[Collection("BootstrapHost")]
public sealed unsafe class GenericDispatchTests : IDisposable
{
    private static int s_lastOpcode, s_lastArgsLen, s_lastOutCap;

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;

    public GenericDispatchTests()
    {
        s_lastOpcode = s_lastArgsLen = s_lastOutCap = 0;

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
    private static int InvokeRecorder(int opcode, nint argsPtr, int argsLen, nint outBuf, int outCap)
    {
        s_lastOpcode = opcode;
        s_lastArgsLen = argsLen;
        s_lastOutCap = outCap;

        if (outCap >= 1)
            *(byte*)outBuf = 0xAB;

        return 1;
    }

    [Fact]
    public void Raw_Invoke_calls_the_client_binding_with_its_arguments()
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(_pluginsRoot);

        var cb = new ClientBindings
        {
            GenericInvokeFn = (nint)(delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)&InvokeRecorder,
        };
        bridge.InstallClientBindingsForTest(cb);

        var raw = bridge.Plugins[0].Raw;
        Span<byte> args = [1, 2, 3];
        Span<byte> result = stackalloc byte[4];

        int written = raw.Invoke(42, args, result);

        written.Should().Be(1);
        s_lastOpcode.Should().Be(42);
        s_lastArgsLen.Should().Be(3);
        s_lastOutCap.Should().Be(4);
        result[0].Should().Be(0xAB);
    }

    [Fact]
    public void GenericEvent_fans_out_to_every_plugin()
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(_pluginsRoot);

        int? receivedId = null;
        byte[]? receivedPayload = null;
        bridge.Plugins[0].Raw.Event += (id, payload) => { receivedId = id; receivedPayload = payload; };

        bridge.TestRaiseGenericEvent(7, [9, 8, 7]);

        receivedId.Should().Be(7);
        receivedPayload.Should().Equal(9, 8, 7);
    }
}
