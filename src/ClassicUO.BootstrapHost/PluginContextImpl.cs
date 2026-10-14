// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.InteropServices;
using System.Text;
using ClassicUO.PluginAbi;
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
    private readonly HighlightImpl _highlight;
    private readonly NameplatesImpl _nameplates;
    private readonly ProfilesImpl _profiles;
    private readonly ClientImpl _client;
    private readonly DispatcherImpl _dispatcher;
    private readonly BuffsImpl _buffs;
    private readonly ScreenTimersImpl _screenTimers;
    private readonly RawChannelImpl _raw;

    public PluginContextImpl(HostBridge bridge, PluginRegistration registration, string folderName, string pluginsRoot)
    {
        Id = registration.Id;
        UODataPath = bridge.UODataPath;
        PluginDataPath = EnsurePluginDataPath(pluginsRoot, folderName);

        _packets    = new PacketPipelineImpl(bridge);
        _input      = new InputRouterImpl();
        _actions    = new GameActionsImpl(bridge);
        _statusBars = new StatusBarsImpl(bridge);
        _highlight  = new HighlightImpl(bridge);
        _nameplates = new NameplatesImpl(bridge);
        _profiles   = new ProfilesImpl(bridge);
        _client     = new ClientImpl(bridge);
        _dispatcher = new DispatcherImpl(bridge);
        _buffs = new BuffsImpl(bridge);
        _screenTimers = new ScreenTimersImpl(bridge);
        _raw = new RawChannelImpl(bridge);
    }

    public string Id { get; }
    public string UODataPath { get; }
    public string PluginDataPath { get; }
    public uint ClientVersion { get; private set; }

    public IPacketPipeline Packets => _packets;
    public IInputRouter Input => _input;
    public IGameActions Actions => _actions;
    public IStatusBars StatusBars => _statusBars;
    public IHighlight Highlight => _highlight;
    public INameplates Nameplates => _nameplates;
    public IProfiles Profiles => _profiles;
    public IClient Client => _client;
    public IDispatcher Game => _dispatcher;
    public IPluginBuffs Buffs => _buffs;
    public IScreenTimers ScreenTimers => _screenTimers;
    public IRawChannel Raw => _raw;

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
    public void RaiseGenericEvent(int eventId, byte[] payload)
    {
        _raw.RaiseEvent(eventId, payload);
        _statusBars.RaiseGenericEvent(eventId, payload);
        _profiles.RaiseGenericEvent(eventId, payload);
    }
    public void RaiseTick()                  => Tick?.Invoke();
    public void RaiseClosing()               => Closing?.Invoke();
    public void RaiseMouse(int button, int wheel) => _input.RaiseMouse(button, wheel);
    public bool RaiseHotkey(int key, int mod, bool pressed) => _input.RaiseHotkey(key, mod, pressed);
    public void RaisePacket(ReadOnlySpan<byte> data, ref bool block, bool incoming) => _packets.Raise(data, ref block, incoming);

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

    public unsafe void Attack(uint serial)
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            InvokeAttack(fn, serial);
        else
            _bridge.PostToGameThread(() => InvokeAttack(fn, serial));
    }

    private static unsafe void InvokeAttack(nint fn, uint serial)
    {
        ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(
            GenericOpcodes.Attack, (nint)(&serial), sizeof(uint), 0, 0);
    }

    public event Action<WalkState>? WalkProgress;
    internal void RaiseWalkProgress(WalkState state) => WalkProgress?.Invoke(state);
}

internal sealed class StatusBarsImpl : IStatusBars
{
    private readonly HostBridge _bridge;
    public StatusBarsImpl(HostBridge bridge) { _bridge = bridge; }

    public event Action<uint>? Opened;
    public event Action<uint>? Closed;
    public event Action<StatusBarChangeInfo>? Changed;

    internal void RaiseGenericEvent(int eventId, byte[] payload)
    {
        switch (eventId)
        {
            case GenericEventIds.StatusBarOpened:
                if (payload.Length >= sizeof(uint))
                    Opened?.Invoke(BitConverter.ToUInt32(payload, 0));
                break;

            case GenericEventIds.StatusBarClosed:
                if (payload.Length >= sizeof(uint))
                    Closed?.Invoke(BitConverter.ToUInt32(payload, 0));
                break;

            case GenericEventIds.StatusBarChanged:
                if (payload.Length >= Marshal.SizeOf<StatusBarChangedPayload>())
                {
                    var p = MemoryMarshal.Read<StatusBarChangedPayload>(payload);
                    Changed?.Invoke(new StatusBarChangeInfo(
                        p.Serial, p.Hits, p.HitsMax, p.Mana, p.ManaMax, p.Stamina, p.StaminaMax,
                        (PluginApi.StatusBarFlags)p.Flags, p.Active != 0, p.GroupId, p.X, p.Y));
                }
                break;
        }
    }

    public unsafe uint[] ListOpen()
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return [];
        if (!_bridge.IsGameThread)
            throw new InvalidOperationException("ListOpen must be called from the game thread; use IDispatcher.Post.");

        Span<byte> buf = stackalloc byte[4096]; // up to 1024 open bars; ListOpenStatusBars errors (-2) rather than truncating
        int written;
        fixed (byte* o = buf)
        {
            written = ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(GenericOpcodes.ListOpenStatusBars, 0, 0, (nint)o, buf.Length);
        }

        if (written <= 0) return [];

        var result = new uint[written / sizeof(uint)];
        MemoryMarshal.Cast<byte, uint>(buf[..written]).CopyTo(result);
        return result;
    }

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

    public unsafe void SetStatusBarPriority(uint serial, int priority)
    {
        var fn = _bridge.ClientBindings.SetStatusBarPriorityFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, int, void>)fn)(serial, priority);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, int, void>)fn)(serial, priority));
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
        int anchorKind = (int)timer.Anchor;
        uint anchorSerial = timer.AnchorSerial;
        ushort ax = timer.AnchorX, ay = timer.AnchorY;
        sbyte az = timer.AnchorZ;
        short offX = timer.AnchorOffsetX, offY = timer.AnchorOffsetY;
        int graceMs = timer.AnchorGraceMs;
        nint labelPtr = string.IsNullOrEmpty(timer.Label) ? nint.Zero : Marshal.StringToHGlobalAnsi(timer.Label);

        void Call()
        {
            try
            {
                ((delegate* unmanaged[Cdecl]<int, int, int, ushort, int, int, int, int, int, nint, byte, int, uint, ushort, ushort, sbyte, short, short, int, void>)fn)(
                    id, shape, dur, hue, gid, x, y, w, h, labelPtr, showTime,
                    anchorKind, anchorSerial, ax, ay, az, offX, offY, graceMs);
            }
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

internal sealed class ProfilesImpl : IProfiles
{
    private readonly HostBridge _bridge;
    private string? _active;

    public ProfilesImpl(HostBridge bridge) { _bridge = bridge; }

    public event Action<string>? Changed;
    public event Action<CharacterSelection>? CharacterSelected;

    public string? Active => _active;

    internal void RaiseGenericEvent(int eventId, byte[] payload)
    {
        switch (eventId)
        {
            case GenericEventIds.ProfileChanged:
                _active = Encoding.UTF8.GetString(payload);
                Changed?.Invoke(_active);
                break;

            case GenericEventIds.CharacterSelected:
                var parts = Encoding.UTF8.GetString(payload).Split('\n');
                if (parts.Length >= 3)
                    CharacterSelected?.Invoke(new CharacterSelection(parts[0], parts[1], parts[2]));
                break;
        }
    }

    private unsafe int Invoke(int opcode, ReadOnlySpan<byte> args, Span<byte> result)
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return -1;
        if (!_bridge.IsGameThread)
            throw new InvalidOperationException("Profiles must be used from the game thread; use IDispatcher.Post.");

        fixed (byte* a = args)
        fixed (byte* o = result)
        {
            return ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(opcode, (nint)a, args.Length, (nint)o, result.Length);
        }
    }

    public IReadOnlyList<string> List()
    {
        var buf = new byte[16 * 1024];
        int n = Invoke(GenericOpcodes.ListProfiles, default, buf);
        if (n <= 0) return [];
        return Encoding.UTF8.GetString(buf, 0, n).Split('\n');
    }

    public ProfileSwitchResult Switch(string name, bool assignToCharacter = false)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name ?? "");
        var args = new byte[nameBytes.Length + 1];
        args[0] = assignToCharacter ? (byte)1 : (byte)0;
        nameBytes.CopyTo(args, 1);

        return Invoke(GenericOpcodes.SwitchProfile, args, default) switch
        {
            0 => ProfileSwitchResult.Applied,
            1 => ProfileSwitchResult.QueuedForLogin,
            -3 => ProfileSwitchResult.UnknownProfile,
            _ => ProfileSwitchResult.Failed,
        };
    }

    public void ClearOverride() => Invoke(GenericOpcodes.ClearProfileOverride, default, default);
}

internal sealed class RawChannelImpl : IRawChannel
{
    private readonly HostBridge _bridge;
    public RawChannelImpl(HostBridge bridge) { _bridge = bridge; }

    public event Action<int, byte[]>? Event;
    internal void RaiseEvent(int eventId, byte[] payload) => Event?.Invoke(eventId, payload);

    public unsafe int Invoke(int opcode, ReadOnlySpan<byte> args, Span<byte> result)
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return -1;
        if (!_bridge.IsGameThread)
            throw new InvalidOperationException("Raw.Invoke must be called from the game thread; use IDispatcher.Post.");

        fixed (byte* a = args)
        fixed (byte* o = result)
        {
            return ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(opcode, (nint)a, args.Length, (nint)o, result.Length);
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

internal sealed class HighlightImpl : IHighlight
{
    private readonly HostBridge _bridge;
    public HighlightImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void AddArea(
        string id,
        int durationMs = -1,
        HighlightSnap snap = HighlightSnap.Mouse,
        uint anchorSerial = 0,
        ushort hue = 0x0386,
        int rangeX = 3,
        int rangeY = 3,
        HighlightObjectTypes objectTypes = HighlightObjectTypes.All,
        int x = 0,
        int y = 0)
    {
        var fn = _bridge.ClientBindings.AddAreaFn;
        if (fn == 0 || string.IsNullOrEmpty(id)) return;

        nint idPtr = Marshal.StringToHGlobalAnsi(id);
        int snapKind = (int)snap;
        int types = (int)objectTypes;

        void Call()
        {
            try
            {
                ((delegate* unmanaged[Cdecl]<nint, int, int, uint, int, int, ushort, int, int, int, void>)fn)(
                    idPtr, durationMs, snapKind, anchorSerial, x, y, hue, rangeX, rangeY, types);
            }
            finally { Marshal.FreeHGlobal(idPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void RemoveArea(string id)
    {
        var fn = _bridge.ClientBindings.RemoveAreaFn;
        if (fn == 0 || string.IsNullOrEmpty(id)) return;

        nint idPtr = Marshal.StringToHGlobalAnsi(id);

        void Call()
        {
            try { ((delegate* unmanaged[Cdecl]<nint, void>)fn)(idPtr); }
            finally { Marshal.FreeHGlobal(idPtr); }
        }

        if (_bridge.IsGameThread) Call();
        else _bridge.PostToGameThread(Call);
    }

    public unsafe void ClearAreas()
    {
        var fn = _bridge.ClientBindings.ClearAreasFn;
        if (fn == 0) return;
        if (_bridge.IsGameThread) ((delegate* unmanaged[Cdecl]<void>)fn)();
        else _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<void>)fn)());
    }

    // Direct synchronous read (no thread marshal), matching IGameActions.TryGetPlayerPosition:
    // it only reads live state, and off-thread callers need the value immediately.
    public unsafe int GetAreaTimer(string id)
    {
        var fn = _bridge.ClientBindings.GetAreaTimerFn;
        if (fn == 0 || string.IsNullOrEmpty(id)) return 0;

        nint idPtr = Marshal.StringToHGlobalAnsi(id);
        try { return ((delegate* unmanaged[Cdecl]<nint, int>)fn)(idPtr); }
        finally { Marshal.FreeHGlobal(idPtr); }
    }

    public unsafe void AddCharacter(uint serial, ushort hue, bool priorityHighlight = false)
    {
        var fn = _bridge.ClientBindings.AddCharacterFn;
        if (fn == 0) return;
        byte p = priorityHighlight ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, ushort, byte, void>)fn)(serial, hue, p);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, ushort, byte, void>)fn)(serial, hue, p));
    }

    public unsafe void RemoveCharacter(uint serial, bool priorityHighlight = false)
    {
        var fn = _bridge.ClientBindings.RemoveCharacterFn;
        if (fn == 0) return;
        byte p = priorityHighlight ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<uint, byte, void>)fn)(serial, p);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<uint, byte, void>)fn)(serial, p));
    }

    public unsafe void ClearCharacters(bool priorityHighlight = false)
    {
        var fn = _bridge.ClientBindings.ClearCharactersFn;
        if (fn == 0) return;
        byte p = priorityHighlight ? (byte)1 : (byte)0;
        if (_bridge.IsGameThread)
            ((delegate* unmanaged[Cdecl]<byte, void>)fn)(p);
        else
            _bridge.PostToGameThread(() => ((delegate* unmanaged[Cdecl]<byte, void>)fn)(p));
    }
}

internal sealed class NameplatesImpl : INameplates
{
    private readonly HostBridge _bridge;
    public NameplatesImpl(HostBridge bridge) { _bridge = bridge; }

    public unsafe void SetOverride(uint serial, bool visible, byte opacityPercent = 100)
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return;

        var payload = new NameplateOverridePayload
        {
            Serial = serial,
            Visible = visible ? (byte)1 : (byte)0,
            OpacityPercent = opacityPercent > 100 ? (byte)100 : opacityPercent,
        };

        if (_bridge.IsGameThread)
            InvokeSetOverride(fn, payload);
        else
            _bridge.PostToGameThread(() => InvokeSetOverride(fn, payload));
    }

    private static unsafe void InvokeSetOverride(nint fn, NameplateOverridePayload payload)
    {
        ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(
            GenericOpcodes.SetNameplateOverride, (nint)(&payload), sizeof(NameplateOverridePayload), 0, 0);
    }

    public unsafe void ClearOverride(uint serial)
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return;

        if (_bridge.IsGameThread)
            InvokeClearOverride(fn, serial);
        else
            _bridge.PostToGameThread(() => InvokeClearOverride(fn, serial));
    }

    private static unsafe void InvokeClearOverride(nint fn, uint serial)
    {
        ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(
            GenericOpcodes.ClearNameplateOverride, (nint)(&serial), sizeof(uint), 0, 0);
    }

    public unsafe void ClearAllOverrides()
    {
        var fn = _bridge.ClientBindings.GenericInvokeFn;
        if (fn == 0) return;

        if (_bridge.IsGameThread)
            InvokeClearAllOverrides(fn);
        else
            _bridge.PostToGameThread(() => InvokeClearAllOverrides(fn));
    }

    private static unsafe void InvokeClearAllOverrides(nint fn)
    {
        ((delegate* unmanaged[Cdecl]<int, nint, int, nint, int, int>)fn)(
            GenericOpcodes.ClearAllNameplateOverrides, 0, 0, 0, 0);
    }
}
