// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;

namespace ClassicUO.Game.UI.Gumps
{
    internal sealed class PluginAnchorGroupGump : Gump
    {
        internal const int WidgetHeight = 24;

        private const ushort GRAPHIC_NORMAL = 0x25F8;
        private const ushort GRAPHIC_HOVER = 0x25F9;

        private readonly PluginAnchorGroupDef _def;
        private readonly GumpPic _pic;
        private readonly Label _label;
        private int _lastX, _lastY;

        public PluginAnchorGroupGump(World world, PluginAnchorGroupDef def)
            : base(world, 0, 0)
        {
            _def = def;

            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = false;
            WantUpdateSize = false;

            X = def.X;
            Y = def.Y;
            _lastX = X;
            _lastY = Y;

            Add(_pic = new GumpPic(0, 0, GRAPHIC_NORMAL, 0)
            {
                AcceptMouseInput = false
            });

            Add(_label = new Label(def.Label ?? "", true, 0x0481, 0, 1)
            {
                X = _pic.Width + 4,
                Y = 2
            });

            Width = _pic.Width + 4 + _label.Width;
            Height = WidgetHeight;

            RefreshTooltip();
            RefreshLockCue();
        }

        public int GroupId => _def.Id;

        private void RefreshTooltip()
        {
            int count = PluginStatusBarGroups.GetLiveMembers(_def.Id).Count;
            int cap = (_def.Columns < 1 ? 1 : _def.Columns) * (_def.Rows < 1 ? 1 : _def.Rows);
            SetTooltip($"{_def.Label}: {count} / {cap}");
        }

        private void RefreshLockCue()
        {
            // Tint the pic while locked so the state is visible. Hue 0 = normal.
            _pic.Hue = _def.Locked ? (ushort)0x0021 : (ushort)0;
        }

        protected override void OnMouseEnter(int x, int y)
        {
            _pic.Graphic = GRAPHIC_HOVER;
            RefreshTooltip();
            base.OnMouseEnter(x, y);
        }

        protected override void OnMouseExit(int x, int y)
        {
            _pic.Graphic = GRAPHIC_NORMAL;
            base.OnMouseExit(x, y);
        }

        public override void Update()
        {
            base.Update();

            // Drag-follow: when the widget itself moved, shift the group's bars
            // by the same delta so the whole cluster tracks the header.
            if (X != _lastX || Y != _lastY)
            {
                if (_def.Locked)
                {
                    // Locked: snap back, do not move.
                    X = _lastX;
                    Y = _lastY;
                }
                else
                {
                    int dx = X - _lastX;
                    int dy = Y - _lastY;

                    foreach (BaseHealthBarGump bar in PluginStatusBarGroups.GetLiveMembers(_def.Id))
                    {
                        bar.X += dx;
                        bar.Y += dy;
                    }

                    _def.X = X;
                    _def.Y = Y;
                    _lastX = X;
                    _lastY = Y;
                }
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            base.OnMouseUp(x, y, button);

            if (button == MouseButtonType.Left && Keyboard.Shift)
            {
                _def.Locked = !_def.Locked;
                RefreshLockCue();
                return;
            }

            if (button == MouseButtonType.Right && Keyboard.Shift)
            {
                var members = new List<BaseHealthBarGump>(PluginStatusBarGroups.GetLiveMembers(_def.Id));

                foreach (BaseHealthBarGump bar in members)
                {
                    PluginStatusBars.CloseStatusBar(bar.LocalSerial);
                }

                RefreshTooltip();
            }
        }
    }
}
