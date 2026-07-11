// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace ClassicUO.BootstrapHost.Tests;

/// <summary>
/// Verifies that GameActionsImpl.WalkTo / StopWalk invoke the cuo-side
/// ClientBindings function pointers with the right arguments. We inject a
/// ClientBindings table whose WalkToFn / StopWalkFn point at managed cdecl
/// recorders, then call the plugin-facing IGameActions surface.
/// </summary>
[Collection("BootstrapHost")]
public sealed unsafe class ActionBindingTests : IDisposable
{
    private static int s_x, s_y, s_z, s_dist;
    private static bool s_run, s_stopCalled;

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;

    public ActionBindingTests()
    {
        s_x = s_y = s_z = s_dist = 0;
        s_run = s_stopCalled = false;

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
    private static byte WalkToRecorder(int x, int y, int z, int distance, byte run)
    {
        s_x = x; s_y = y; s_z = z; s_dist = distance; s_run = run != 0;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StopWalkRecorder() => s_stopCalled = true;

    [Fact]
    public void WalkTo_invokes_the_client_binding_with_its_arguments()
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(_pluginsRoot); // sets game thread to this thread

        var cb = new ClientBindings
        {
            WalkToFn   = (nint)(delegate* unmanaged[Cdecl]<int, int, int, int, byte, byte>)&WalkToRecorder,
            StopWalkFn = (nint)(delegate* unmanaged[Cdecl]<void>)&StopWalkRecorder,
        };
        bridge.InstallClientBindingsForTest(cb);

        var actions = bridge.Plugins[0].Actions;

        actions.WalkTo(10, 20, 5, 1, run: true).Should().BeTrue();
        s_x.Should().Be(10);
        s_y.Should().Be(20);
        s_z.Should().Be(5);
        s_dist.Should().Be(1);
        s_run.Should().BeTrue();
    }

    [Fact]
    public void StopWalk_invokes_the_client_binding()
    {
        var bridge = new HostBridge();
        bridge.LoadPluginsForTest(_pluginsRoot);

        var cb = new ClientBindings
        {
            StopWalkFn = (nint)(delegate* unmanaged[Cdecl]<void>)&StopWalkRecorder,
        };
        bridge.InstallClientBindingsForTest(cb);

        bridge.Plugins[0].Actions.StopWalk();

        s_stopCalled.Should().BeTrue();
    }
}
