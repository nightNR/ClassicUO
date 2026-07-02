// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    internal enum TimerShape { Circle, Bar, Numeric }
    internal enum StackDirection { Down, Up, Right, Left }

    internal struct ScreenTimerEntry
    {
        public int Id;
        public TimerShape Shape;
        public long StartTicks;
        public int DurationMs;
        public ushort Hue;
        public int GroupId;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public string Label;
        public bool ShowTime;
        public int Order;   // insertion order within its group
    }

    internal readonly struct TimerGroup
    {
        public readonly int GroupId;
        public readonly int X;
        public readonly int Y;
        public readonly StackDirection Direction;
        public readonly int Gap;

        public TimerGroup(int groupId, int x, int y, StackDirection direction, int gap)
        {
            GroupId = groupId;
            X = x;
            Y = y;
            Direction = direction;
            Gap = gap;
        }
    }

    /// <summary>
    /// Plugin-driven screen timers keyed by int id, plus their stacking groups.
    /// Storage + pure layout math only; expiry detection and event dispatch live
    /// in <see cref="PluginTimersManager"/>. Layout is a pure function of the
    /// entry, its group, and the member extent, so it is unit-testable without
    /// rendering.
    /// </summary>
    internal static class ScreenTimers
    {
        private const int DefaultBarW = 120, DefaultBarH = 14;
        private const int DefaultCircle = 32;
        private const int DefaultNumericW = 40, DefaultNumericH = 20;

        private static readonly Dictionary<int, ScreenTimerEntry> _timers = new Dictionary<int, ScreenTimerEntry>();
        private static readonly Dictionary<int, TimerGroup> _groups = new Dictionary<int, TimerGroup>();
        private static readonly Dictionary<int, int> _nextOrderByGroup = new Dictionary<int, int>();

        public static IReadOnlyDictionary<int, ScreenTimerEntry> Entries => _timers;

        public static void DefineGroup(int groupId, int x, int y, StackDirection dir, int gap)
        {
            if (groupId == 0)
                return;
            _groups[groupId] = new TimerGroup(groupId, x, y, dir, gap);
        }

        public static bool TryGetGroup(int groupId, out TimerGroup group) => _groups.TryGetValue(groupId, out group);

        public static void AddOrUpdate(int id, TimerShape shape, int durationMs, ushort hue, int groupId,
                                       int x, int y, int width, int height, string label, bool showTime, long now)
        {
            int order;
            if (_timers.TryGetValue(id, out var existing))
                order = existing.Order;          // update in place, keep stack slot
            else
            {
                // new -> append to end of this group's stack order
                _nextOrderByGroup.TryGetValue(groupId, out order);
                _nextOrderByGroup[groupId] = order + 1;
            }

            _timers[id] = new ScreenTimerEntry
            {
                Id = id,
                Shape = shape,
                StartTicks = now,                // set/restart
                DurationMs = durationMs,
                Hue = hue,
                GroupId = groupId,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Label = label ?? string.Empty,
                ShowTime = showTime,
                Order = order,
            };
        }

        public static bool Remove(int id) => _timers.Remove(id);

        public static void RemoveGroup(int groupId, List<int> removedIdsInto)
        {
            foreach (var kv in _timers)
                if (kv.Value.GroupId == groupId)
                    removedIdsInto.Add(kv.Key);

            foreach (var id in removedIdsInto)
                _timers.Remove(id);

            _groups.Remove(groupId);
            _nextOrderByGroup.Remove(groupId);
        }

        public static void CollectExpired(long now, List<int> into)
        {
            foreach (var kv in _timers)
            {
                var e = kv.Value;
                if (e.DurationMs > 0 && now >= e.StartTicks + e.DurationMs)
                    into.Add(e.Id);
            }
        }

        public static void Clear()
        {
            _timers.Clear();
            _groups.Clear();
            _nextOrderByGroup.Clear();
        }

        /// <summary>Test-only: reset all state.</summary>
        public static void Reset() => Clear();

        public static float RemainingFraction(in ScreenTimerEntry e, long now)
        {
            if (e.DurationMs <= 0)
                return 0f;
            float f = 1f - (now - e.StartTicks) / (float)e.DurationMs;
            if (f < 0f) return 0f;
            if (f > 1f) return 1f;
            return f;
        }

        public static int DefaultExtent(TimerShape shape, in StackDirection dir, int width, int height)
        {
            bool vertical = dir == StackDirection.Down || dir == StackDirection.Up;
            (int w, int h) = DefaultSize(shape);
            if (width > 0) w = width;
            if (height > 0) h = height;
            return vertical ? h : w;
        }

        public static (int w, int h) DefaultSize(TimerShape shape) => shape switch
        {
            TimerShape.Bar => (DefaultBarW, DefaultBarH),
            TimerShape.Circle => (DefaultCircle, DefaultCircle),
            _ => (DefaultNumericW, DefaultNumericH),
        };

        public static (int x, int y) ComputePosition(in ScreenTimerEntry e, in TimerGroup group, int extent)
        {
            if (e.GroupId == 0 || group.GroupId == 0)
                return (e.X, e.Y);

            int step = e.Order * (extent + group.Gap);
            return group.Direction switch
            {
                StackDirection.Down  => (group.X, group.Y + step),
                StackDirection.Up    => (group.X, group.Y - step),
                StackDirection.Right => (group.X + step, group.Y),
                StackDirection.Left  => (group.X - step, group.Y),
                _ => (group.X, group.Y),
            };
        }
    }
}
