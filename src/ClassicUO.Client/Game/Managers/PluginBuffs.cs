// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Game.Data;

namespace ClassicUO.Game.Managers
{
    /// <summary>Immutable snapshot of a plugin-owned buff icon.</summary>
    internal readonly struct PluginBuffEntry
    {
        public readonly int Id;
        public readonly ushort Graphic;
        public readonly long ExpiryTicks;   // long.MaxValue == infinite
        public readonly BuffDisplayKind Kind;
        public readonly string Text;

        public PluginBuffEntry(int id, ushort graphic, long expiryTicks, BuffDisplayKind kind, string text)
        {
            Id = id;
            Graphic = graphic;
            ExpiryTicks = expiryTicks;
            Kind = kind;
            Text = text ?? string.Empty;
        }

        public bool IsInfinite => ExpiryTicks == long.MaxValue;
    }

    /// <summary>
    /// Plugin-driven buff icons, keyed by the plugin's int id, rendered
    /// alongside server buffs in <see cref="UI.Gumps.BuffGump"/>. Storage only;
    /// expiry detection and event dispatch live in <see cref="PluginTimersManager"/>.
    /// </summary>
    internal static class PluginBuffs
    {
        private static readonly Dictionary<int, PluginBuffEntry> _buffs = new Dictionary<int, PluginBuffEntry>();

        public static IReadOnlyDictionary<int, PluginBuffEntry> Entries => _buffs;

        public static void AddOrUpdate(int id, ushort graphic, int durationMs, BuffDisplayKind kind, string text, long now)
        {
            long expiry = durationMs <= 0 ? long.MaxValue : now + durationMs;
            _buffs[id] = new PluginBuffEntry(id, graphic, expiry, kind, text);
        }

        public static bool Remove(int id) => _buffs.Remove(id);

        public static void CollectExpired(long now, List<int> into)
        {
            foreach (var kv in _buffs)
            {
                var e = kv.Value;
                if (!e.IsInfinite && now >= e.ExpiryTicks)
                    into.Add(e.Id);
            }
        }

        public static void Clear() => _buffs.Clear();

        /// <summary>Test-only: drop all entries so tests start clean.</summary>
        public static void Reset() => _buffs.Clear();
    }
}
