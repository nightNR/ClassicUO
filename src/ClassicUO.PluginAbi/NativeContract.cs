// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.InteropServices;

namespace ClassicUO.PluginAbi;

/// <summary>
/// Canonical v2 native ABI contract between cuo (the client, exporting
/// <c>Initialize</c>) and a v2 apphost (<c>ClassicUO.BootstrapHost</c>, or an
/// external host such as Phoenix's <c>Phoenix.BootstrapHost</c>). Both sides
/// must reference this project directly rather than maintaining their own
/// copy of these structs — a hand-copied mirror can silently drift out of
/// sync (wrong field order = wrong function called, no compiler error).
///
/// This contract is v2-only. The legacy v1 apphost (<c>ClassicUO.Bootstrap</c>,
/// net472) and external v1 plugin binaries (e.g. <c>cuoapi.dll</c>) keep their
/// own frozen, independently-maintained copy of this layout and are not
/// expected to reference this package.
///
/// Append-only: never insert a field in the middle of either struct, and
/// never reorder existing fields. Doing so shifts every field after it,
/// silently corrupting any consumer still built against the old layout —
/// a crash inside whatever function pointer field lands wherever the caller
/// expects, with no compiler error. Always append new fields at the end
/// (before <see cref="ClientBindings.ClientVersion"/>, which must stay last).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct HostBindings
{
    public nint InitializeFn;
    public nint LoadPluginFn;
    public nint TickFn;
    public nint ClosingFn;
    public nint FocusGainedFn;
    public nint FocusLostFn;
    public nint ConnectedFn;
    public nint DisconnectedFn;
    public nint HotkeyFn;
    public nint MouseFn;
    public nint CmdListFn;
    public nint SdlEventFn;
    public nint UpdatePlayerPosFn;
    public nint PacketInFn;
    public nint PacketOutFn;
    public nint WalkProgressFn;      // void(int state) — auto-walk state transition
    public nint BuffEventFn;         // void(int id, int reason) — plugin buff removed
    public nint TimerEventFn;        // void(int id, int reason) — screen timer removed
    // Generic event push (cuo -> host -> plugin), keyed by an opcode-style event
    // id instead of a dedicated field. New event kinds are added as new event ids,
    // not new struct fields — see GenericDispatch.cs.
    public nint GenericEventFn;      // void(int eventId, byte* payloadPtr, int payloadLen)
}

[StructLayout(LayoutKind.Sequential)]
public struct ClientBindings
{
    public nint PluginRecvFn;        // bool(nint data, ref int length) — inject to client
    public nint PluginSendFn;        // bool(nint data, ref int length) — inject to server
    public nint PacketLengthFn;      // short(int id)
    public nint CastSpellFn;         // void(int index)
    public nint SetWindowTitleFn;    // void(nint utf8Ptr)
    public nint GetClilocFn;         // nint(int id, nint utf8ArgsPtr, bool capitalize)
    public nint RequestMoveFn;       // bool(int dir, bool run)
    public nint GetPlayerPositionFn; // bool(out int x, out int y, out int z)
    public nint ReflectionCmdFn;     // legacy reflection commands; unused by v2
    public nint WalkToFn;            // bool(int x, int y, int z, int distance, byte run)
    public nint StopWalkFn;          // void()
    public nint OpenStatusBarFn;     // void(uint serial, int x, int y, byte moveIfExists, int groupId)
    public nint CloseStatusBarFn;    // void(uint serial)
    public nint SetOverlayFn;        // void(uint serial, ushort hue, ushort backgroundHue)
    public nint AddBuffFn;           // void(int id, ushort graphic, int durationMs, int kind, nint textUtf8)
    public nint RemoveBuffFn;        // void(int id)
    public nint ClearBuffsFn;        // void()
    public nint DefineTimerGroupFn;  // void(int groupId, int x, int y, int direction, int gap)
    public nint AddTimerFn;          // void(int id, int shape, int durationMs, ushort hue, int groupId,
                                      //      int x, int y, int width, int height, nint labelUtf8, byte showTime,
                                      //      int anchorKind, uint anchorSerial, ushort ax, ushort ay, sbyte az,
                                      //      short offX, short offY, int graceMs)
    public nint RemoveTimerFn;       // void(int id)
    public nint RemoveTimerGroupFn;  // void(int groupId)
    public nint ClearTimersFn;       // void()
    public nint SetPluginPartyMemberFn;    // void(uint serial, ushort hue, int x, int y, int hp, int map)
    public nint RemovePluginPartyMemberFn; // void(uint serial)
    public nint ClearPluginPartyFn;        // void()
    public nint CheckLosFn;      // byte(int fx,int fy,int fz, int tx,int ty,int tz, int map, byte includeDynamic)
    public nint CheckLosBatchFn; // void(int fx,int fy,int fz, int* toXYZ, int count, int map, byte includeDynamic, byte* results)
    public nint AddAreaFn;             // void(nint idUtf8, int durationMs, int snapKind, uint anchorSerial, int x, int y, ushort hue, int rangeX, int rangeY, int objectTypes)
    public nint RemoveAreaFn;          // void(nint idUtf8)
    public nint ClearAreasFn;          // void()
    public nint GetAreaTimerFn;        // int(nint idUtf8)
    public nint AddCharacterFn;        // void(uint serial, ushort hue, byte priorityHighlight)
    public nint RemoveCharacterFn;     // void(uint serial, byte priorityHighlight)
    public nint ClearCharactersFn;     // void(byte priorityHighlight)
    public nint SetStatusBarPriorityFn; // void(uint serial, int priority)
    // Generic request/response dispatch (plugin/host -> cuo), keyed by an
    // opcode instead of a dedicated field. New capabilities are added as new
    // opcodes handled inside this one entry point, not new struct fields —
    // see GenericDispatch.cs. Must stay before ClientVersion (append-only).
    public nint GenericInvokeFn;     // int(int opcode, byte* argsPtr, int argsLen, byte* outBuf, int outCap)
    // Packed client version (major<<24|minor<<16|build<<8|revision). MUST stay
    // last — appended to preserve ABI, populated in Initialize() after UO.Load
    // has parsed it.
    public int ClientVersion;
}
