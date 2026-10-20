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

    /// <summary>
    /// Attacks a mobile the same way the client's own attack action does:
    /// sends the attack-request packet AND updates cuo's local attack-target
    /// state (last-attack/new-target-system serial), so client-side UI that
    /// reacts to "who am I attacking" (e.g. the new-target-system frame)
    /// picks it up even though the server never confirms it. Args: <c>uint</c>
    /// serial. Result: none (0 bytes written on success).
    /// </summary>
    public const int Attack = 2;

    /// <summary>
    /// Forces serial's nameplate (Object Handles name+HP box) visible at a
    /// given opacity, or hidden, overriding the player's own name-overhead
    /// filters/toggle and any manual close. Replaces any existing override
    /// for the same serial. Args: <see cref="NameplateOverridePayload"/>.
    /// Result: none.
    /// </summary>
    public const int SetNameplateOverride = 3;

    /// <summary>
    /// Removes the nameplate override for one serial, reverting to normal
    /// (player-controlled) behavior. Args: <c>uint</c> serial. Result: none.
    /// </summary>
    public const int ClearNameplateOverride = 4;

    /// <summary>
    /// Removes every nameplate override, regardless of which plugin set it
    /// (matches <c>ClearCharacters</c>/<c>ClearAreas</c>: no per-plugin
    /// partitioning). Args: none. Result: none.
    /// </summary>
    public const int ClearAllNameplateOverrides = 5;

    /// <summary>
    /// Lists the client's named profiles. Args: none. Result: UTF-8 names separated by <c>'\n'</c>
    /// (no trailing separator; 0 bytes when there are none). -2 when the output buffer is too small.
    /// </summary>
    public const int ListProfiles = 6;

    /// <summary>
    /// Name of the profile currently active for the logged-in character. Args: none. Result: UTF-8 name,
    /// 0 bytes when the character still uses a legacy per-character profile or nothing is loaded yet.
    /// </summary>
    public const int GetActiveProfile = 7;

    /// <summary>
    /// Switches the client profile. Args: <c>byte</c> flags (bit 0 = also store as the character's assigned
    /// profile) followed by the UTF-8 profile name. In game the switch is applied immediately (game thread only);
    /// before login it is queued as an override for the next login (flags ignored). Result: none; returns
    /// 0 = applied now, 1 = queued for the next login, -2 = args too small, -3 = unknown profile, -4 = switch failed.
    /// A runtime override lasts for the session and is cleared on logout.
    /// </summary>
    public const int SwitchProfile = 8;

    /// <summary>Drops a queued/session profile override so the character's assigned profile applies again on next load. Args: none. Result: none.</summary>
    public const int ClearProfileOverride = 9;
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

    /// <summary>
    /// The active profile changed: after a login load or a live switch (fired once the UI is rebuilt).
    /// Payload: UTF-8 profile name.
    /// </summary>
    public const int ProfileChanged = 4;

    /// <summary>
    /// The player picked/created a character on the login screen, before the profile is resolved. A host can answer
    /// with <see cref="GenericOpcodes.SwitchProfile"/> to override the profile for this login.
    /// Payload: UTF-8 <c>account\nserver\ncharacter</c>.
    /// </summary>
    public const int CharacterSelected = 5;

    /// <summary>
    /// The set of named profiles changed (created, renamed or deleted). No payload; re-query with
    /// <see cref="GenericOpcodes.ListProfiles"/>.
    /// </summary>
    public const int ProfileListChanged = 6;
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

/// <summary>
/// Wire payload for <see cref="GenericOpcodes.SetNameplateOverride"/>. Fixed
/// layout, blitted directly — no serialization step on either side.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NameplateOverridePayload
{
    public uint Serial;
    /// <summary>0 = force-hidden, 1 = force-visible.</summary>
    public byte Visible;
    /// <summary>0-100. Meaningful only when <see cref="Visible"/> is 1.</summary>
    public byte OpacityPercent;
}
