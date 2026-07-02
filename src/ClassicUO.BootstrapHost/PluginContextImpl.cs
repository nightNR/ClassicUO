// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.InteropServices;
using System.Text;
using ClassicUO.PluginApi;

namespace ClassicUO.BootstrapHost;

/// <summary>One <see cref="IPluginContext"/> per loaded plugin.</summary>
internal sealed class PluginContextImpl : IPluginContext
{
    private IPlugin? _plugin;

    private readonly PacketPipelineImpl _packets;
    private readonly InputRouterImpl _input;
    private readonly GameActionsImpl _actions;
    private readonly StatusBarsImpl _statusBars;
    private readonly ClientImpl _client;
    private readonly DispatcherImpl _dispatcher;
    private readonly BuffsImpl _buffs;
    private readonly ScreenTimersImpl _screenTimers;

    public PluginContextImpl(HostBridge bridge, PluginRegistration registration, string folderName, string pluginsRoot)
    {
        Id = registration.Id;
        UODataPath = ResolveUODataPath();
        PluginDataPath = EnsurePluginDataPath(pluginsRoot, folderName);

        _packets    = new PacketPipelineImpl(bridge);
        _input      = new InputRouterImpl();
        _actions    = new GameActionsImpl(bridge);
        _statusBars = new StatusBarsImpl(bridge);
        _client     = new ClientImpl(bridge);
        _dispatcher = new DispatcherImpl(bridge);
        _buffs = new BuffsImpl(bridge);
        _screenTimers = new ScreenTimersImpl(bridge);
    }

    public string Id { get; }
    public string UODataPath { get; }
    public string PluginDataPath { get; }
    public uint ClientVersion { get; private set; }

    public IPacketPipeline Packets => _packets;
    public IInputRouter Input => _input;
    public IGameActions Actions => _actions;
    public IStatusBars StatusBars => _statusBars;
    public IClient Client => _client;
    public IDispatcher Game => _dispatcher;
    public IPluginBuffs Buffs => _buffs;
    public IScreenTimers ScreenTimers => _screenTimers;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action? FocusGained;
    public event Action? FocusLost;
    public event Action<int, int, int>? PlayerPositionChanged;
    public event Action? Tick;
    public event Action? Closing;

    public void AttachPlugin(IPlugin plugin)
    {
        _plugin = plugin;
    }

    public void AttachHost()
    {
        // Invoked after HostBridge.OnInitialize so the bridge has its game
        // thread and ClientBindings populated when the plugin first runs.
        _plugin?.OnInitialize(this);
    }

    public void RaiseShutdown()
    {
        _plugin?.OnShutdown();
    }

    public void RaiseConnected()             => Connected?.Invoke();
    public void RaiseDisconnected()          => Disconnected?.Invoke();
    public void RaiseFocusGained()           => FocusGained?.Invoke();
    public void RaiseFocusLost()             => FocusLost?.Invoke();
    public void RaisePlayerPositionChanged(int x, int y, int z) => PlayerPositionChanged?.Invoke(x, y, z);
    public void RaiseWalkProgress(WalkState state) => _actions.RaiseWalkProgress(state);
    public void RaiseBuffEvent(int id, int reason) => _buffs.RaiseEvent(id, reason);
    public void RaiseTimerEvent(int id, int reason) => _screenTimers.RaiseEvent(id, reason);
    public void RaiseTick()                  => Tick?.Invoke();
    public void RaiseClosing()               => Closing?.Invoke();
    public void RaiseMouse(int button, int wheel) => _input.RaiseMouse(button, wheel);
    public bool RaiseHotkey(int key, int mod, bool pressed) => _input.RaiseHotkey(key, mod, pressed);
    public void RaisePacket(ReadOnlySpan<byte> data, ref bool block, bool incoming) => _packets.Raise(data, ref block, incoming);

    private static string ResolveUODataPath() => AppContext.BaseDirectory;

    private static string EnsurePluginDataPath(string pluginsRoot, string folderName)
    {
        var path = Path.Combine(pluginsRoot, folderName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Test-only: lets the smoke test inspect the loaded plugin instance.</summary>
    internal IPlugin? PluginInstance => _plugin;
}

internal sealed class PacketPipelineImpl : IPacketPipeline
{
    private readonly HostBridge _bridge;

    public PacketPipelineImpl(HostBridge bridge) { _bridge = bridge; }

    public event PacketHandler? Incoming;
    public event PacketHandler? Outgoing;

    public void SendToClient(ReadOnlySpan<byte> data) => Inject(data, _bridge.ClientBindings.PluginRecvFn);
    public void SendToServer(ReadOnlySpan<byte> data) => Inject(data, _bridge.ClientBindings.PluginSendFn);

    public unsafe short GetPacketLength(byte packetId)
    {
        var fn = _bridge.ClientBindings.PacketLengthFn;
        if (fn == 0) return 0;
        return ((delegate* unmanaged[Cdecl]<int, short>)fn)(packetId);
    }

    public void Raise(ReadOnlySpan<byte> data, ref bool block, bool incoming)
    {
        // Direct multicast invoke: a ref-struct (Span<T>) can't cross the
        // GetInvocationList iterator boundary. The PacketHandler contract
        // makes block sticky-true so multicast aggregation is safe here.
        var handler = incoming ? Incoming : Outgoing;
        handler?.Invoke(data, ref block);
    }

    private static unsafe void Inject(ReadOnlySpan<byte> data, nint fnPtr)
    {
        if (fnPtr == 0 || data.IsEmpty) return;
        var fn = (delegate* unmanaged[Cdecl]<nint, int*, byte>)fnPtr;
        fixed (byte* p = data)
        {
            int len = data.Length;
            fn((nint)p, &len);
        }
    }
}

internal sealed class InputRouterImpl : IInputRouter
{
    public event HotkeyHandler? Hotkey;
    public event MouseHandler? Mouse;

    public bool RaiseHotkey(int key, int mod, bool pressed)
    {
        var h = Hotkey;
        if (h is null) return true;
        bool allow = true;
        foreach (HotkeyHandler d in h.GetInvocationList())
            if (!d(key, mod, pressed)) allow = false;
        return allow;
    }

    public void RaiseMouse(int button, int wheel) => Mouse?.Invoke(button, wheel);
}

internal sealed class GameActionsImpl : IGameActions
{
    private readonly HostBridge _bridge;
    public GameActionsImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void CastSpell(int spellIndex)
    {
        var fn = _bridge.ClientBindings.CastSpellFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<int, void>)fn)(spellIndex);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<int, void>)fn)(spellIndex));
    }

    public unsafe bool RequestMove(int direction, bool run)
    {
        var fn = _bridge.ClientBindings.RequestMoveFn;
        if (fn == 0) return false;
        if (!_bridge.IsGameThread)
            throw new InvalidOperationException("RequestMove must be called from the game thread; use IDispatcher.Post.");
        return ((delegate* unmanaged[Cdecl]<int, byte, byte>)fn)(direction, run ? (byte)1 : (byte)0) != 0;
    }

    public unsafe bool TryGetPlayerPosition(out int x, out int y, out int z)
    {
        var fn = _bridge.ClientBindings.GetPlayerPositionFn;
        x = y = z = 0;
        if (fn == 0) return false;
        int xx = 0, yy = 0, zz = 0;
        bool ok = ((delegate* unmanaged[Cdecl]<int*, int*, int*, byte>)fn)(&xx, &yy, &zz) != 0;
        x = xx; y = yy; z = zz;
        return ok;
    }

    public unsafe bool WalkTo(int x, int y, int z, int distance, bool run)
    {
        var fn = _bridge.ClientBindings.WalkToFn;
        if (fn == 0) return false;
        if (!_bridge.IsGameThread)
            throw new InvalidOperationException("WalkTo must be called from the game thread; use IDispatcher.Post.");
        return ((delegate* unmanaged[Cdecl]<int, int, int, int, byte, byte>)fn)(x, y, z, distance, run ? (byte)1 : (byte)0) != 0;
    }

    public unsafe void StopWalk()
    {
        var fn = _bridge.ClientBindings.StopWalkFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<void>)fn)();
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }

    public event Action<WalkState>? WalkProgress;
    internal void RaiseWalkProgress(WalkState state) => WalkProgress?.Invoke(state);
}

internal sealed class StatusBarsImpl : IStatusBars
{
    private readonly HostBridge _bridge;
    public StatusBarsImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void OpenStatusBar(uint serial, int x, int y, bool moveIfExists = true, int groupId = 0)
    {
        var fn = _bridge.ClientBindings.OpenStatusBarFn;
        if (fn == 0) return;
        byte move = moveIfExists ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, int, int, byte, int, void>)fn)(serial, x, y, move, groupId);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, int, int, byte, int, void>)fn)(serial, x, y, move, groupId));
    }

    public unsafe void CloseStatusBar(uint serial)
    {
        var fn = _bridge.ClientBindings.CloseStatusBarFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, void>)fn)(serial);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, void>)fn)(serial));
    }

    public unsafe void SetOverlay(uint serial, ushort hue, ushort backgroundHue = 0)
    {
        var fn = _bridge.ClientBindings.SetOverlayFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, ushort, ushort, void>)fn)(serial, hue, backgroundHue);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, ushort, ushort, void>)fn)(serial, hue, backgroundHue));
    }
}

internal sealed class BuffsImpl : IPluginBuffs
{
    private readonly HostBridge _bridge;
    public BuffsImpl(HostBridge bridge) { _bridge = bridge; }

    public event Action<int>? Expired;
    public event Action<int, BuffRemoveReason>? Removed;

    internal void RaiseEvent(int id, int reason)
    {
        var r = (BuffRemoveReason)reason;
        if (r == BuffRemoveReason.Expired)
            Expired?.Invoke(id);
        Removed?.Invoke(id, r);
    }

    public unsafe void AddOrUpdate(BuffConfig config)
    {
        var fn = _bridge.ClientBindings.AddBuffFn;
        if (fn == 0 || config is null) return;

        int id = config.Id;
        ushort graphic = config.Graphic;
        int dur = config.DurationMs;
        int kind = (int)config.Kind;
        nint textPtr = string.IsNullOrEmpty(config.Text) ? nint.Zero : Marshal.StringToHGlobalAnsi(config.Text);

        void Call()
        {
            try { ((delegate* unmanaged[Cdecl]<int, ushort, int, int, nint, void>)fn)(id, graphic, dur, kind, textPtr); }
            finally { if (textPtr != nint.Zero) Marshal.FreeHGlobal(textPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void Remove(int id)
    {
        var fn = _bridge.ClientBindings.RemoveBuffFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<int, void>)fn)(id);
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<int, void>)fn)(id));
    }

    public unsafe void ClearAll()
    {
        var fn = _bridge.ClientBindings.ClearBuffsFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<void>)fn)();
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }
}

internal sealed class ScreenTimersImpl : IScreenTimers
{
    private readonly HostBridge _bridge;
    public ScreenTimersImpl(HostBridge bridge) { _bridge = bridge; }

    public event Action<int>? Expired;
    public event Action<int, TimerRemoveReason>? Removed;

    internal void RaiseEvent(int id, int reason)
    {
        var r = (TimerRemoveReason)reason;
        if (r == TimerRemoveReason.Expired)
            Expired?.Invoke(id);
        Removed?.Invoke(id, r);
    }

    public unsafe void DefineGroup(TimerGroupConfig group)
    {
        var fn = _bridge.ClientBindings.DefineTimerGroupFn;
        if (fn == 0 || group is null) return;
        int gid = group.GroupId, x = group.X, y = group.Y, dir = (int)group.Direction, gap = group.Gap;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<int, int, int, int, int, void>)fn)(gid, x, y, dir, gap);
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<int, int, int, int, int, void>)fn)(gid, x, y, dir, gap));
    }

    public unsafe void AddOrUpdate(TimerConfig timer)
    {
        var fn = _bridge.ClientBindings.AddTimerFn;
        if (fn == 0 || timer is null) return;

        int id = timer.Id, shape = (int)timer.Shape, dur = timer.DurationMs, gid = timer.GroupId;
        ushort hue = timer.Hue;
        int x = timer.X, y = timer.Y, w = timer.Width, h = timer.Height;
        byte showTime = timer.ShowTime ? (byte)1 : (byte)0;
        nint labelPtr = string.IsNullOrEmpty(timer.Label) ? nint.Zero : Marshal.StringToHGlobalAnsi(timer.Label);

        void Call()
        {
            try { ((delegate* unmanaged[Cdecl]<int, int, int, ushort, int, int, int, int, int, nint, byte, void>)fn)(id, shape, dur, hue, gid, x, y, w, h, labelPtr, showTime); }
            finally { if (labelPtr != nint.Zero) Marshal.FreeHGlobal(labelPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void Remove(int id) => CallById(_bridge.ClientBindings.RemoveTimerFn, id);
    public unsafe void RemoveGroup(int groupId) => CallById(_bridge.ClientBindings.RemoveTimerGroupFn, groupId);

    public unsafe void ClearAll()
    {
        var fn = _bridge.ClientBindings.ClearTimersFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<void>)fn)();
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }

    private unsafe void CallById(nint fn, int id)
    {
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<int, void>)fn)(id);
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<int, void>)fn)(id));
    }
}

internal sealed class ClientImpl : IClient
{
    private readonly HostBridge _bridge;
    public ClientImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void SetWindowTitle(string title)
    {
        var fn = _bridge.ClientBindings.SetWindowTitleFn;
        if (fn == 0 || title is null) return;

        var byteCount = Encoding.UTF8.GetByteCount(title);
        Span<byte> buf = byteCount + 1 <= 1024
            ? stackalloc byte[byteCount + 1]
            : new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(title, buf);
        buf[byteCount] = 0;
        fixed (byte* p = buf)
            ((delegate* unmanaged[Cdecl]<nint, void>)fn)((nint)p);
    }

    public unsafe string? GetCliloc(int id, string args = "", bool capitalize = false)
    {
        var fn = _bridge.ClientBindings.GetClilocFn;
        if (fn == 0) return null;

        var argsPtr = string.IsNullOrEmpty(args)
            ? nint.Zero
            : Marshal.StringToHGlobalAnsi(args);
        try
        {
            var resultPtr = ((delegate* unmanaged[Cdecl]<int, nint, byte, nint>)fn)(id, argsPtr, capitalize ? (byte)1 : (byte)0);
            return resultPtr == 0 ? null : Marshal.PtrToStringAnsi(resultPtr);
        }
        finally
        {
            if (argsPtr != 0) Marshal.FreeHGlobal(argsPtr);
        }
    }
}

internal sealed class DispatcherImpl : IDispatcher
{
    private readonly HostBridge _bridge;
    public DispatcherImpl(HostBridge bridge) { _bridge = bridge; }

    public bool IsGameThread => _bridge.IsGameThread;

    public void Post(Action action) => _bridge.PostToGameThread(action);

    public Task PostAsync(Action action)
    {
        if (_bridge.IsGameThread)
        {
            try { action(); return Task.CompletedTask; }
            catch (Exception ex) { return Task.FromException(ex); }
        }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.PostToGameThread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
