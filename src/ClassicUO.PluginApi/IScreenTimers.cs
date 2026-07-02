// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.PluginApi;

/// <summary>Visual shape of a screen timer.</summary>
public enum TimerShape { Circle, Bar, Numeric }

/// <summary>Direction stacking timers grow from their group anchor.</summary>
public enum StackDirection { Down, Up, Right, Left }

/// <summary>Placement intent. Reserved; a <c>GroupId == 0</c> already implies Lone.</summary>
public enum PlacementMode { Lone, Stacking }

/// <summary>Why a screen timer was removed.</summary>
public enum TimerRemoveReason { Expired, RemovedByPlugin, RemovedByUser }

/// <summary>Immutable description of a screen timer. Flattened to scalars by the host.</summary>
public sealed class TimerConfig
{
    public int Id { get; init; }              // required, plugin-chosen key
    public TimerShape Shape { get; init; }    // required
    public int DurationMs { get; init; }      // required, > 0
    public ushort Hue { get; init; }
    public int GroupId { get; init; }         // 0 = lone
    public int X { get; init; }               // used only when GroupId == 0
    public int Y { get; init; }
    public int Width { get; init; }           // 0 = default per shape
    public int Height { get; init; }          // 0 = default per shape
    public string? Label { get; init; }
    public bool ShowTime { get; init; }
}

/// <summary>Layout definition for a stacking timer group.</summary>
public sealed class TimerGroupConfig
{
    public int GroupId { get; init; }         // required, non-zero
    public int X { get; init; }               // group anchor
    public int Y { get; init; }
    public StackDirection Direction { get; init; }
    public int Gap { get; init; }             // pixels between members
}

/// <summary>
/// Plugin-driven on-screen timer overlay. Timers are fixed-position and
/// non-interactive. All methods auto-marshal to the game thread. Setting an
/// existing id restarts that timer with the new duration.
/// </summary>
public interface IScreenTimers
{
    /// <summary>Defines or updates a stacking group's anchor, direction, and gap.</summary>
    void DefineGroup(TimerGroupConfig group);

    /// <summary>Adds a timer, or updates+restarts the running timer when the id exists.</summary>
    void AddOrUpdate(TimerConfig timer);

    /// <summary>Removes the timer with <paramref name="id"/> if present.</summary>
    void Remove(int id);

    /// <summary>Removes every timer belonging to <paramref name="groupId"/> and the group.</summary>
    void RemoveGroup(int groupId);

    /// <summary>Removes every timer and group owned by this plugin.</summary>
    void ClearAll();

    /// <summary>Raised with the timer id when a timer expires.</summary>
    event Action<int> Expired;

    /// <summary>Raised with the timer id and reason on any removal (including expiry).</summary>
    event Action<int, TimerRemoveReason> Removed;
}
