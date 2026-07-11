// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.PluginApi;

/// <summary>Kind of a plugin buff, controlling its tint in the BuffGump.</summary>
public enum BuffDisplayKind { None, Buff, Debuff }

/// <summary>Why a plugin buff was removed.</summary>
public enum BuffRemoveReason { Expired, RemovedByPlugin, RemovedByUser }

/// <summary>Immutable description of a plugin buff. Flattened to scalars by the host.</summary>
public sealed class BuffConfig
{
    public int Id { get; init; }              // required, plugin-chosen key
    public ushort Graphic { get; init; }      // required, gump graphic id
    public int DurationMs { get; init; }      // 0 = infinite
    public BuffDisplayKind Kind { get; init; }
    public string? Text { get; init; }
}

/// <summary>
/// Adds/updates/removes plugin buff icons that render alongside server buffs in
/// the client's BuffGump. All methods auto-marshal to the game thread. The
/// plugin owns lifecycle; buffs are not persisted across relogs.
/// </summary>
public interface IPluginBuffs
{
    /// <summary>Adds a buff, or updates in place when <see cref="BuffConfig.Id"/> already exists.</summary>
    void AddOrUpdate(BuffConfig config);

    /// <summary>Removes the buff with <paramref name="id"/> if present.</summary>
    void Remove(int id);

    /// <summary>Removes every plugin buff owned by this plugin.</summary>
    void ClearAll();

    /// <summary>Raised with the buff id when a buff expires.</summary>
    event Action<int> Expired;

    /// <summary>Raised with the buff id and reason on any removal (including expiry).</summary>
    event Action<int, BuffRemoveReason> Removed;
}
