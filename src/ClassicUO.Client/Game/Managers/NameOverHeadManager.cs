// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers
{
    [Flags]
    internal enum NameOverheadTypeAllowed
    {
        None = 0,
        Items = 1 << 0,
        Corpses = 1 << 1,
        Innocent = 1 << 2,
        Ally = 1 << 3,
        Gray = 1 << 4,
        Criminal = 1 << 5,
        Enemy = 1 << 6,
        Murderer = 1 << 7,
        Invulnerable = 1 << 8,
        MyCorpse = 1 << 9,
        AllMobiles = Innocent | Ally | Gray | Criminal | Enemy | Murderer | Invulnerable,
        All = Items | Corpses | AllMobiles | MyCorpse
    }

    internal sealed class NameOverHeadManager
    {
        private NameOverHeadHandlerGump _gump;
        private readonly World _world;

        public NameOverHeadManager(World world) { _world = world; }

        public NameOverheadTypeAllowed TypeAllowed
        {
            get => ProfileManager.CurrentProfile.NameOverheadTypeAllowed;
            set => ProfileManager.CurrentProfile.NameOverheadTypeAllowed = value;
        }

        public bool IsToggled
        {
            get => ProfileManager.CurrentProfile.NameOverheadToggled;
            set => ProfileManager.CurrentProfile.NameOverheadToggled = value;
        }

        private readonly Dictionary<uint, (bool Visible, byte OpacityPercent)> _overrides = new();

        /// <summary>
        /// Forces serial's nameplate visible (at <paramref name="opacityPercent"/>,
        /// clamped 0-100) or hidden, replacing any existing override for the
        /// same serial.
        /// </summary>
        public void SetOverride(uint serial, bool visible, byte opacityPercent) =>
            _overrides[serial] = (visible, opacityPercent > 100 ? (byte)100 : opacityPercent);

        /// <summary>Removes the override for serial, if any.</summary>
        public void ClearOverride(uint serial) => _overrides.Remove(serial);

        /// <summary>Removes every override, regardless of which plugin set it.</summary>
        public void ClearAllOverrides() => _overrides.Clear();

        /// <summary>
        /// Looks up the override for serial. Returns <c>false</c> (with
        /// <paramref name="visible"/> = false, <paramref name="opacityPercent"/>
        /// = 100) when no override is set.
        /// </summary>
        public bool TryGetOverride(uint serial, out bool visible, out byte opacityPercent)
        {
            if (_overrides.TryGetValue(serial, out var o))
            {
                visible = o.Visible;
                opacityPercent = o.OpacityPercent;
                return true;
            }

            visible = false;
            opacityPercent = 100;
            return false;
        }

        public bool IsAllowed(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            if (SerialHelper.IsItem(entity.Serial))
            {
                if (entity is Item item)
                {
                    if (item.IsCorpse)
                    {
                        if (_world.CorpseManager.OwnCorpses.Contains(item.Serial) && TypeAllowed.HasFlag(NameOverheadTypeAllowed.MyCorpse))
                            return true;

                        return TypeAllowed.HasFlag(NameOverheadTypeAllowed.Corpses);
                    }

                    if (!GameObject.CanBeDrawn(entity.World, item.DisplayedGraphic))
                        return false;
                }

                return TypeAllowed.HasFlag(NameOverheadTypeAllowed.Items);
            }

            if (SerialHelper.IsMobile(entity.Serial) && entity is Mobile mobile)
            {
                return mobile.NotorietyFlag switch
                {
                    NotorietyFlag.Innocent => TypeAllowed.HasFlag(NameOverheadTypeAllowed.Innocent),
                    NotorietyFlag.Ally => TypeAllowed.HasFlag(NameOverheadTypeAllowed.Ally),
                    NotorietyFlag.Gray => TypeAllowed.HasFlag(NameOverheadTypeAllowed.Gray),
                    NotorietyFlag.Criminal => TypeAllowed.HasFlag(NameOverheadTypeAllowed.Criminal),
                    NotorietyFlag.Enemy => TypeAllowed.HasFlag(NameOverheadTypeAllowed.Enemy),
                    NotorietyFlag.Murderer => TypeAllowed.HasFlag(NameOverheadTypeAllowed.Murderer),
                    NotorietyFlag.Invulnerable => TypeAllowed.HasFlag(NameOverheadTypeAllowed.Invulnerable),
                    _ => true
                };
            }

            return false;
        }

        public void Open()
        {
            if (_gump == null || _gump.IsDisposed)
            {
                _gump = new NameOverHeadHandlerGump(_world);
                UIManager.Add(_gump);
            }

            _gump.IsEnabled = true;
            _gump.IsVisible = true;
        }

        public void SetMenuVisible(bool visible)
        {
            if (_gump != null && !_gump.IsDisposed)
            {
                _gump.IsVisible = visible;
            }
        }

        public void Close()
        {
            if (_gump == null)
            { //Required in case nameplates are active when closing and reopening the client
                _gump = new NameOverHeadHandlerGump(_world);
                UIManager.Add(_gump);
            }


            _gump.IsEnabled = false;
            _gump.IsVisible = false;
        }

        public void ToggleOverheads()
        {
            IsToggled = !IsToggled;
        }
    }
}
