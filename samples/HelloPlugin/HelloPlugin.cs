// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;

using ClassicUO.PluginApi;

namespace HelloPlugin;

/// <summary>
/// Module-load registration for the sample plugin. The
/// <see cref="ModuleInitializerAttribute"/> ensures <see cref="Register"/>
/// runs as soon as the host triggers the module constructor — no reflection,
/// no <c>[Plugin]</c> attribute scan.
/// </summary>
internal static class HelloPluginRegistration
{
    // CA2255 warns that ModuleInitializer is unusual in libraries; for v2
    // plugins it is the documented contract — the host triggers the module
    // ctor explicitly so that registration runs at discovery without
    // reflection.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register()
    {
        PluginRegistry.Register(new PluginRegistration(
            id: "HelloPlugin",
            factory: static () => new HelloPlugin(),
            name: "Hello Plugin",
            version: "1.0.0",
            description: "Sample plugin that logs every event for the smoke tests."));
    }
}

/// <summary>
/// Minimal sample plugin used by the smoke tests. Subscribes to every
/// lifecycle event the v2 contract exposes and appends a one-line entry to
/// the log path provided via the <c>CUO_PLUGIN_TEST_LOG</c> environment
/// variable. Real plugins would do something more interesting with the
/// events, but the surface exercised here mirrors any well-behaved plugin.
/// </summary>
public sealed class HelloPlugin : IPlugin
{
    private string? _logPath;
    private readonly object _logLock = new();

    private IPluginContext? _ctx;
    private int _retries;
    private (int x, int y, int z, int dist, bool run) _goal;
    private const int MaxRetries = 5;

    public void OnInitialize(IPluginContext context)
    {
        _logPath = Environment.GetEnvironmentVariable("CUO_PLUGIN_TEST_LOG");
        Log("OnInitialize");
        _ctx = context;

        context.Connected             += () => Log("Connected");
        context.Disconnected          += () => Log("Disconnected");
        context.FocusGained           += () => Log("FocusGained");
        context.FocusLost             += () => Log("FocusLost");
        context.PlayerPositionChanged += (x, y, z) => Log($"Pos:{x},{y},{z}");
        context.Tick                  += () => Log("Tick");
        context.Closing               += () => Log("Closing");

        context.Actions.WalkProgress  += state => Log($"Walk:{state}");

        if (Environment.GetEnvironmentVariable("CUO_PLUGIN_WALK_DEMO") is { Length: > 0 } target
            && TryParseGoal(target, out _goal))
        {
            context.Connected += () => context.Game.Post(() =>
            {
                _retries = 0;
                Log($"WalkDemo:start {_goal.x},{_goal.y},{_goal.z} dist={_goal.dist} run={_goal.run}");
                _ctx!.Actions.WalkTo(_goal.x, _goal.y, _goal.z, _goal.dist, _goal.run);
            });

            context.Actions.WalkProgress += state =>
            {
                if (state == ClassicUO.PluginApi.WalkState.Blocked && _retries++ < MaxRetries)
                {
                    Log($"WalkDemo:reroute attempt={_retries}");
                    _ctx!.Game.Post(() =>
                        _ctx!.Actions.WalkTo(_goal.x, _goal.y, _goal.z, _goal.dist, _goal.run));
                }
            };
        }

        context.Input.Mouse  += (button, wheel) => Log($"Mouse:{button}/{wheel}");
        context.Input.Hotkey += (key, mod, pressed) =>
        {
            Log($"Hotkey:{key}/{mod}/{pressed}");

            // Manual status-bar demo (press-only). Replace 0x40000000 with a
            // real serial when testing against a live shard.
            if (pressed)
            {
                const uint demoSerial = 0x40000000;
                switch (key)
                {
                    case 1: // open a grouped pair
                        context.StatusBars.OpenStatusBar(demoSerial, 200, 200, true, 1);
                        context.StatusBars.OpenStatusBar(demoSerial + 1, 200, 260, true, 1);
                        break;
                    case 2: // priority highlight (UO hue 0x0021 = red-ish)
                        context.StatusBars.SetOverlay(demoSerial, 0x0021);
                        break;
                    case 3: // clear highlight
                        context.StatusBars.SetOverlay(demoSerial, 0);
                        break;
                    case 4: // close
                        context.StatusBars.CloseStatusBar(demoSerial);
                        context.StatusBars.CloseStatusBar(demoSerial + 1);
                        break;
                }
            }

            // Block the dedicated test key 999; allow everything else.
            return key != 999;
        };

        context.Packets.Incoming += (ReadOnlySpan<byte> p, ref bool block) =>
        {
            Log($"PacketIn:len={p.Length},id=0x{(p.Length > 0 ? p[0] : (byte)0):X2}");
            // Block packet id 0x99 as a test signal.
            if (p.Length > 0 && p[0] == 0x99) block = true;
        };
    }

    public void OnShutdown() => Log("OnShutdown");

    // "x,y,z,dist,run" e.g. "1450,1670,0,1,true"
    private static bool TryParseGoal(string s, out (int x, int y, int z, int dist, bool run) goal)
    {
        goal = default;
        var p = s.Split(',');
        if (p.Length != 5) return false;
        if (!int.TryParse(p[0], out var x) || !int.TryParse(p[1], out var y) ||
            !int.TryParse(p[2], out var z) || !int.TryParse(p[3], out var d) ||
            !bool.TryParse(p[4], out var run))
            return false;
        goal = (x, y, z, d, run);
        return true;
    }

    private void Log(string line)
    {
        if (_logPath is null) return;
        lock (_logLock)
            File.AppendAllText(_logPath, line + Environment.NewLine);
    }
}
