// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>Which buff kinds a <see cref="BuffGump"/> instance displays.</summary>
    internal enum BuffGumpMode
    {
        All = 0,    // joined: every icon
        Buffs = 1,  // buffs + neutral (None)
        Debuffs = 2 // debuffs only
    }

    /// <summary>Rendering input for one buff icon, from either a server BuffIcon or a plugin buff.</summary>
    internal readonly struct BuffEntryInput
    {
        public readonly ushort Graphic;
        public readonly long Timer;          // 0xFFFF_FFFF == infinite
        public readonly string Text;
        public readonly Data.BuffDisplayKind Kind;
        public readonly string TooltipId;    // "ID: <type>" or "ID: <pluginId>"

        public BuffEntryInput(ushort graphic, long timer, string text, Data.BuffDisplayKind kind, string tooltipId)
        {
            Graphic = graphic;
            Timer = timer;
            Text = text ?? string.Empty;
            Kind = kind;
            TooltipId = tooltipId ?? string.Empty;
        }
    }

    internal class BuffGump : Gump
    {
        private GumpPic _background;
        private Button _button;
        private GumpDirection _direction;
        private ushort _graphic;
        private DataBox _box;
        private int _shiftX, _shiftY;
        private BuffGumpMode _mode = BuffGumpMode.All;

        // Vertical gap between the Buffs and Debuffs windows when first split.
        private const int SplitFallbackOffsetY = 100;

        public BuffGumpMode Mode => _mode;

        public BuffGump(World world) : base(world, 0, 0)
        {
            CanMove = true;
            CanCloseWithRightClick = true;
            AcceptMouseInput = true;
        }

        public BuffGump(World world, int x, int y) : this(world, x, y, BuffGumpMode.All)
        {
        }

        public BuffGump(World world, int x, int y, BuffGumpMode mode) : this(world)
        {
            _mode = mode;
            // Distinct serial per mode so the two split windows save/restore
            // independently; All stays 0 to preserve existing saved layouts.
            LocalSerial = (uint)mode;

            X = x;
            Y = y;

            _direction = GumpDirection.LEFT_HORIZONTAL;
            _graphic = 0x7580;

            SetInScreen();

            BuildGump();
        }

        public override GumpType GumpType => GumpType.Buff;

        public override void Dispose()
        {
            base.Dispose();

            // Drop the shared refresh hook only when the last buff window closes.
            if (!Managers.UIManager.Gumps.OfType<BuffGump>().Any(g => g != this && !g.IsDisposed))
            {
                Managers.PluginTimersManager.GumpRefresh = null;
            }
        }

        /// <summary>Rebuilds every open buff window; used by the shared refresh hook.</summary>
        public static void RequestUpdateContentsAll()
        {
            foreach (BuffGump g in Managers.UIManager.Gumps.OfType<BuffGump>())
            {
                g.RequestUpdateContents();
            }
        }

        /// <summary>
        /// Status-window button action: open the joined gump when none exist,
        /// otherwise toggle between joined (single) and split (Buffs + Debuffs).
        /// </summary>
        public static void ToggleFromStatusButton(World world)
        {
            var list = Managers.UIManager.Gumps.OfType<BuffGump>().Where(g => !g.IsDisposed).ToList();

            if (list.Count == 0)
            {
                Managers.UIManager.Add(new BuffGump(world, 100, 100, BuffGumpMode.All));
                return;
            }

            BuffGump joined = list.FirstOrDefault(g => g.Mode == BuffGumpMode.All);

            if (joined != null)
            {
                // Joined -> split. Buffs inherits the joined position; Debuffs
                // offsets below it.
                int x = joined.X;
                int y = joined.Y;
                joined.Dispose();

                var buffs = new BuffGump(world, x, y, BuffGumpMode.Buffs);
                Managers.UIManager.Add(buffs);

                int offset = buffs.Height > 0 ? buffs.Height + 5 : SplitFallbackOffsetY;
                Managers.UIManager.Add(new BuffGump(world, x, y + offset, BuffGumpMode.Debuffs));
            }
            else
            {
                // Split -> join. New joined window inherits the Buffs position.
                BuffGump buffs = list.FirstOrDefault(g => g.Mode == BuffGumpMode.Buffs) ?? list[0];
                int x = buffs.X;
                int y = buffs.Y;

                foreach (BuffGump g in list)
                {
                    g.Dispose();
                }

                Managers.UIManager.Add(new BuffGump(world, x, y, BuffGumpMode.All));
            }
        }

        private bool Accepts(Data.BuffDisplayKind kind)
        {
            switch (_mode)
            {
                case BuffGumpMode.Buffs:
                    return kind != Data.BuffDisplayKind.Debuff; // buff + neutral
                case BuffGumpMode.Debuffs:
                    return kind == Data.BuffDisplayKind.Debuff;
                default:
                    return true;
            }
        }

        private void BuildGump()
        {
            // Undo previous shift to restore anchor position
            X -= _shiftX;
            Y -= _shiftY;
            _shiftX = 0;
            _shiftY = 0;

            WantUpdateSize = true;

            _box?.Clear();
            _box?.Children.Clear();

            Clear();

            Add(_background = new GumpPic(0, 0, _graphic, 0) { LocalSerial = 1 });

            Add(
                _button = new Button(0, 0x7585, 0x7589, 0x7589)
                {
                    ButtonAction = ButtonAction.Activate
                }
            );

            switch (_direction)
            {
                case GumpDirection.LEFT_HORIZONTAL:
                    _button.X = -2;
                    _button.Y = 36;

                    break;

                case GumpDirection.RIGHT_VERTICAL:
                    _button.X = 34;
                    _button.Y = 78;

                    break;

                case GumpDirection.RIGHT_HORIZONTAL:
                    _button.X = 76;
                    _button.Y = 36;

                    break;

                case GumpDirection.LEFT_VERTICAL:
                default:
                    _button.X = 0;
                    _button.Y = 0;

                    break;
            }

            Add(_box = new DataBox(0, 0, 0, 0) { WantUpdateSize = true });

            if (World.Player != null)
            {
                foreach (var k in World.Player.BuffIcons)
                {
                    BuffIcon icon = World.Player.BuffIcons[k.Key];

                    if (!Accepts(icon.Kind))
                    {
                        continue;
                    }

                    _box.Add(new BuffControlEntry(new BuffEntryInput(
                        icon.Graphic, icon.Timer, icon.Text, icon.Kind, $"ID: {icon.Type}")));
                }
            }

            foreach (var kv in Managers.PluginBuffs.Entries)
            {
                var e = kv.Value;

                if (!Accepts(e.Kind))
                {
                    continue;
                }

                long timer = e.IsInfinite ? 0xFFFF_FFFF : e.ExpiryTicks;

                // Accept a BuffIconType id (e.g. 1078 = Surge) and resolve it to a
                // gump graphic like the server path does; fall back to treating the
                // value as a raw gump graphic for custom icons outside the table.
                ushort graphic = Data.BuffTable.TryResolveIcon(e.Graphic, out ushort mapped)
                    ? mapped
                    : e.Graphic;

                _box.Add(new BuffControlEntry(new BuffEntryInput(
                    graphic, timer, e.Text, e.Kind, $"ID: {e.Id}")));
            }

            _background.Graphic = _graphic;
            _background.X = 0;
            _background.Y = 0;

            UpdateElements();

            // Wire the shared refresh hook on every build so both construction and
            // restore paths keep plugin-buff expiry updates flowing to all windows.
            Managers.PluginTimersManager.GumpRefresh = RequestUpdateContentsAll;
        }

        public override void Save(XmlTextWriter writer)
        {
            // Save the anchor position (un-shifted)
            X -= _shiftX;
            Y -= _shiftY;
            base.Save(writer);
            X += _shiftX;
            Y += _shiftY;

            writer.WriteAttributeString("graphic", _graphic.ToString());
            writer.WriteAttributeString("direction", ((int)_direction).ToString());
            writer.WriteAttributeString("mode", ((int)_mode).ToString());
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            _graphic = ushort.Parse(xml.GetAttribute("graphic"));
            _direction = (GumpDirection)byte.Parse(xml.GetAttribute("direction"));

            string modeAttr = xml.GetAttribute("mode");
            _mode = string.IsNullOrEmpty(modeAttr)
                ? BuffGumpMode.All
                : (BuffGumpMode)int.Parse(modeAttr);
            LocalSerial = (uint)_mode;

            BuildGump();
        }
        protected override void UpdateContents()
        {
            BuildGump();
        }

        private void UpdateElements()
        {
            int count = _box.Children.Count;

            // Position icons at their natural locations
            for (int i = 0, offset = 0; i < count; i++, offset += 31)
            {
                Control e = _box.Children[i];

                switch (_direction)
                {
                    case GumpDirection.LEFT_VERTICAL:
                        e.X = 25;
                        e.Y = 26 + offset;

                        break;

                    case GumpDirection.LEFT_HORIZONTAL:
                        e.X = 26 + offset;
                        e.Y = 5;

                        break;

                    case GumpDirection.RIGHT_VERTICAL:
                        e.X = 5;
                        e.Y = _background.Height - 48 - offset;

                        break;

                    case GumpDirection.RIGHT_HORIZONTAL:
                        e.X = _background.Width - 48 - offset;
                        e.Y = 5;

                        break;
                }
            }

            // Find if any icons have negative positions (RIGHT variants with many icons)
            int minX = 0, minY = 0;

            for (int i = 0; i < count; i++)
            {
                Control e = _box.Children[i];

                if (e.X < minX)
                    minX = e.X;

                if (e.Y < minY)
                    minY = e.Y;
            }

            // If icons extend beyond origin, shift everything so all coords are non-negative,
            // then move the gump origin to compensate (keeping the background at the same screen position).
            if (minX < 0 || minY < 0)
            {
                int shiftX = minX < 0 ? -minX : 0;
                int shiftY = minY < 0 ? -minY : 0;

                for (int i = 0; i < count; i++)
                {
                    _box.Children[i].X += shiftX;
                    _box.Children[i].Y += shiftY;
                }

                _background.X += shiftX;
                _background.Y += shiftY;
                _button.X += shiftX;
                _button.Y += shiftY;

                _shiftX = -shiftX;
                _shiftY = -shiftY;
                X += _shiftX;
                Y += _shiftY;
            }

            // Explicitly size the box to encompass all icon positions.
            int boxW = 0, boxH = 0;

            for (int i = 0; i < count; i++)
            {
                Control e = _box.Children[i];
                int right = e.X + e.Width;
                int bottom = e.Y + e.Height;

                if (right > boxW)
                    boxW = right;

                if (bottom > boxH)
                    boxH = bottom;
            }

            _box.Width = boxW;
            _box.Height = boxH;
        }

        public override void OnButtonClick(int buttonID)
        {
            if (buttonID == 0)
            {
                _graphic++;

                if (_graphic > 0x7582)
                {
                    _graphic = 0x757F;
                }

                switch (_graphic)
                {
                    case 0x7580:
                        _direction = GumpDirection.LEFT_HORIZONTAL;

                        break;

                    case 0x7581:
                        _direction = GumpDirection.RIGHT_VERTICAL;

                        break;

                    case 0x7582:
                        _direction = GumpDirection.RIGHT_HORIZONTAL;

                        break;

                    case 0x757F:
                    default:
                        _direction = GumpDirection.LEFT_VERTICAL;

                        break;
                }

                RequestUpdateContents();
            }
        }

        private enum GumpDirection
        {
            LEFT_VERTICAL,
            LEFT_HORIZONTAL,
            RIGHT_VERTICAL,
            RIGHT_HORIZONTAL
        }

        private class BuffControlEntry : GumpPic
        {
            private byte _alpha;
            private bool _decreaseAlpha;
            private readonly RenderedText _gText;
            private float _updateTooltipTime;
            private readonly long _timer;
            private readonly string _text;
            private readonly string _tooltipId;
            private readonly Data.BuffDisplayKind _kind;

            public BuffControlEntry(BuffEntryInput input) : base(0, 0, input.Graphic, 0)
            {
                if (IsDisposed)
                {
                    return;
                }

                _timer = input.Timer;
                _text = input.Text;
                _tooltipId = input.TooltipId;
                _kind = input.Kind;
                _alpha = 0xFF;
                _decreaseAlpha = true;

                _gText = RenderedText.Create(
                    "",
                    0xFFFF,
                    2,
                    true,
                    FontStyle.Fixed | FontStyle.BlackBorder,
                    TEXT_ALIGN_TYPE.TS_CENTER,
                    Width
                );

                AcceptMouseInput = true;
                WantUpdateSize = false;
                CanMove = true;

                SetTooltip(_text + "\n" + _tooltipId);
            }

            public override void Update()
            {
                base.Update();

                if (!IsDisposed)
                {
                    int delta = (int)(_timer - Time.Ticks);

                    if (_updateTooltipTime < Time.Ticks && delta > 0)
                    {
                        TimeSpan span = TimeSpan.FromMilliseconds(delta);

                        SetTooltip(
                            string.Format(
                                ResGumps.TimeLeft,
                                _text + "\n" + _tooltipId,
                                span.Hours,
                                span.Minutes,
                                span.Seconds
                            )
                        );

                        _updateTooltipTime = (float)Time.Ticks + 1000;

                        if (span.Hours > 0)
                        {
                            _gText.Text = string.Format(ResGumps.Span0Hours, span.Hours);
                        }
                        else
                        {
                            _gText.Text =
                                span.Minutes > 0
                                    ? $"{span.Minutes}:{span.Seconds:00}"
                                    : $"{span.Seconds:00}s";
                        }
                    }

                    if (_timer != 0xFFFF_FFFF && delta < 10000)
                    {
                        if (delta <= 0)
                        {
                            ((BuffGump)Parent.Parent)?.RequestUpdateContents();
                        }
                        else
                        {
                            int alpha = _alpha;
                            int addVal = (10000 - delta) / 600;

                            if (_decreaseAlpha)
                            {
                                alpha -= addVal;

                                if (alpha <= 60)
                                {
                                    _decreaseAlpha = false;
                                    alpha = 60;
                                }
                            }
                            else
                            {
                                alpha += addVal;

                                if (alpha >= 255)
                                {
                                    _decreaseAlpha = true;
                                    alpha = 255;
                                }
                            }

                            _alpha = (byte)alpha;
                        }
                    }
                }
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float layerDepth = layerDepthRef;

                ushort hue = _kind switch
                {
                    Data.BuffDisplayKind.Debuff => 0x0021,   // red
                    Data.BuffDisplayKind.Buff => 0x0044,     // green
                    _ => (ushort)0,
                };
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(hue, false, _alpha / 255f, true);

                ref readonly var gumpInfo = ref Client.Game.UO.Gumps.GetGump(Graphic);
                var texture = gumpInfo.Texture;
                if (texture != null)
                {

                    var sourceRectangle = gumpInfo.UV;
                    renderLists.AddGumpWithAtlas
                    (
                        (batcher) =>
                        {
                            batcher.Draw(texture, new Vector2(x, y), sourceRectangle, hueVector, layerDepth);
                            return true;
                        }
                    );
                    if (
                        ProfileManager.CurrentProfile != null
                        && ProfileManager.CurrentProfile.BuffBarTime
                    )
                    {
                        renderLists.AddGumpNoAtlas
                    (
                        (batcher) =>
                        {
                            _gText.Draw(batcher, x - 3, y + sourceRectangle.Height / 2 - 3, hueVector.Z);
                            return true;
                        }
                    );
                    }
                }

                return true;
            }

            public override void Dispose()
            {
                _gText?.Destroy();
                base.Dispose();
            }
        }
    }
}
