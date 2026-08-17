// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.InteropServices;

namespace ClassicUO.PluginAbi;

/// <summary>
/// Opcodes for <see cref="ClientBindings.GenericInvokeFn"/> — the plugin/host
/// calls into cuo, cuo returns a byte count written to the caller's output
/// buffer (or a negative error code: -1 unknown opcode, -2 output buffer too
/// small). Add new capabilities as new opcodes here instead of new
/// <see cref="ClientBindings"/> fields.
/// </summary>
public static class GenericOpcodes
{
    /// <summary>
    /// Capability negotiation. Args: none. Result: a packed <c>int[]</c> of
    /// every opcode this cuo build supports (including this one). Callers
    /// should probe this once after connect/init and only invoke opcodes the
    /// result actually lists — an older cuo build simply won't list newer ones.
    /// </summary>
    public const int GetSupportedOpcodes = 0;

    /// <summary>
    /// Lists every currently-open status (health) bar, plugin-opened or
    /// player-opened alike. Args: none. Result: a packed <c>uint[]</c> of
    /// serials. Returns -2 (and writes nothing) if the caller's buffer can't
    /// hold every open bar — size for the worst case rather than retrying,
    /// since a partial list would silently hide bars.
    /// </summary>
    public const int ListOpenStatusBars = 1;
}

/// <summary>
/// Event ids for <see cref="HostBindings.GenericEventFn"/> — cuo pushes an
/// event to the host, which fans it out to plugins. Add new event kinds as
/// new ids here instead of new <see cref="HostBindings"/> fields.
/// </summary>
public static class GenericEventIds
{
    /// <summary>A status bar was opened (plugin- or player-driven). Payload: <c>uint</c> serial.</summary>
    public const int StatusBarOpened = 1;

    /// <summary>A status bar was closed (any reason: user, out-of-range, death, plugin). Payload: <c>uint</c> serial.</summary>
    public const int StatusBarClosed = 2;

    /// <summary>A status bar's tracked state changed. Payload: <see cref="StatusBarChangedPayload"/>.</summary>
    public const int StatusBarChanged = 3;
}

/// <summary>Bitmask for <see cref="StatusBarChangedPayload.Flags"/>.</summary>
[Flags]
public enum StatusBarFlags : byte
{
    None = 0,
    Poisoned = 1,
    Paralyzed = 2,
    Invulnerable = 4,
    WarMode = 8,
    YellowHits = 16,
    Dead = 32,
}

/// <summary>
/// Wire payload for <see cref="GenericEventIds.StatusBarChanged"/>. Fixed
/// layout, blitted directly — no serialization step on either side.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct StatusBarChangedPayload
{
    public uint Serial;
    public ushort Hits;
    public ushort HitsMax;
    public ushort Mana;
    public ushort ManaMax;
    public ushort Stamina;
    public ushort StaminaMax;
    public byte Flags; // StatusBarFlags
    /// <summary>1 when the bar has a valid, in-range, living target; 0 when the target is out of range or dead.</summary>
    public byte Active;
    public int GroupId;
    public short X;
    public short Y;
}
