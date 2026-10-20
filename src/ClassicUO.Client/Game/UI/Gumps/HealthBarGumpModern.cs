// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Third status-bar style: framed panel with a name header and icon/bar/value rows, drawn
    /// entirely in code (no gump art). Every size derives from one global scale and every color
    /// from the profile, so the look is freely scalable and recolorable.
    /// </summary>
    internal sealed class HealthBarGumpModern : BaseHealthBarGump
    {
        private const ushort VALUE_HUE = 0x0481; // white
        private const ushort DIM_HUE = 912;

        private ModernBarStyle _style;
        private ModernBarView _view;
        private readonly BarModel _model = new BarModel();
        private Label _hpText, _manaText, _stamText, _distanceText;
        private int _rows;
        private bool _showDistance;
        private ushort _nameHue = VALUE_HUE;
        private int _tHits = -1, _tHitsMax = -1, _tMana = -1, _tManaMax = -1, _tStam = -1, _tStamMax = -1, _tDistance = -1;

        public HealthBarGumpModern(World world, Entity entity) : base(world, entity)
        {
        }

        public HealthBarGumpModern(World world, uint serial) : base(world, serial)
        {
        }

        public HealthBarGumpModern(World world) : base(world, 0, 0)
        {
        }

        /// <summary>Gump size for the current profile, for placing a new bar under the cursor.</summary>
        internal static Point DefaultSize(Profile profile, bool multiRow, bool showDistance)
        {
            var style = new ModernBarStyle(profile);

            return new Point(style.Width(showDistance), style.Height(multiRow ? 3 : 1));
        }

        public override bool Contains(int x, int y) => true;

        protected override void UpdateContents()
        {
            Clear();
            Children.Clear();

            _view = null;
            _hpText = _manaText = _stamText = _distanceText = null;
            _tHits = _tHitsMax = _tMana = _tManaMax = _tStam = _tStamMax = _tDistance = -1;

            if (_textBox != null)
            {
                _textBox.MouseUp -= TextBoxOnMouseUp;
            }

            _textBox = null;

            BuildGump();
        }

        protected override void BuildGump()
        {
            WantUpdateSize = false;

            Profile profile = ProfileManager.CurrentProfile;
            _style = new ModernBarStyle(profile);

            Entity entity = World.Get(LocalSerial);
            Mobile mobile = entity as Mobile;
            bool isPlayer = LocalSerial == World.Player;

            _rows = isPlayer || World.Party.Contains(LocalSerial) || World.PluginParty.Contains(LocalSerial) ? 3 : 1;
            _showDistance = profile.ShowDistanceOnHealthBar && !isPlayer;

            if (entity != null)
            {
                _canChangeName = mobile != null && mobile.IsRenamable;
            }

            NotorietyFlag notoriety = isPlayer ? World.Player.NotorietyFlag : mobile?.NotorietyFlag ?? NotorietyFlag.Gray;
            _nameHue = Notoriety.GetHue(notoriety);

            Width = _style.Width(_showDistance);
            Height = _style.Height(_rows);

            _model.Reset(_style, _rows, _showDistance, Width, Height);
            _model.Notoriety = HueRgb(_nameHue);

            Add(_view = new ModernBarView(_model) { Width = Width, Height = Height, AcceptMouseInput = true, CanMove = true });

            int pad = _style.S(ModernBarStyle.Padding);
            int headerHeight = _style.S(ModernBarStyle.HeaderHeight);

            (byte nameFont, bool nameUnicode) = ResolveFont(ModernBarStyle.NameFontSize);

            Add(
                _textBox = new StbTextBox(
                    nameFont,
                    32,
                    Width - pad * 2,
                    nameUnicode,
                    FontStyle.Cropped | FontStyle.BlackBorder,
                    _nameHue,
                    TEXT_ALIGN_TYPE.TS_CENTER
                )
                {
                    X = pad,
                    Width = Width - pad * 2,
                    IsEditable = false,
                    AcceptMouseInput = _canChangeName,
                    AcceptKeyboardInput = _canChangeName,
                    WantUpdateSize = false,
                    CanMove = true,
                }
            );
            _textBox.Y = (headerHeight - _textBox.Height) / 2 + _style.TextNudge(ModernBarStyle.NameFontSize);
            _textBox.MouseUp += TextBoxOnMouseUp;
            _textBox.SetText(_name);

            if (_style.ShowValues)
            {
                (byte valueFont, bool valueUnicode) = ResolveFont(ModernBarStyle.ValueFontSize);

                Add(_hpText = CreateLabel(valueFont, valueUnicode));

                if (_rows == 3)
                {
                    Add(_manaText = CreateLabel(valueFont, valueUnicode));
                    Add(_stamText = CreateLabel(valueFont, valueUnicode));
                }
            }

            if (_showDistance)
            {
                (byte distFont, bool distUnicode) = ResolveFont(ModernBarStyle.NameFontSize);
                Add(_distanceText = CreateLabel(distFont, distUnicode));
            }

            if (entity == null)
            {
                MarkOutOfRange();
            }
        }

        private Label CreateLabel(byte font, bool unicode)
        {
            return new Label(string.Empty, unicode, ProfileManager.CurrentProfile.ResolveHue(FontLocation.StatusBars, VALUE_HUE), font: font, style: FontStyle.BlackBorder)
            {
                AcceptMouseInput = false,
                CanMove = true,
            };
        }

        // Scaled TTF slot for the bar text, honoring a face chosen for the status-bar font location.
        private (byte slot, bool unicode) ResolveFont(int baseSize)
        {
            Profile profile = ProfileManager.CurrentProfile;
            string face = "interbold";

            if (profile.LocationFonts != null
                && profile.LocationFonts.TryGetValue(FontLocation.StatusBars, out FontChoice choice)
                && !string.IsNullOrEmpty(choice.Face))
            {
                face = choice.Face;
            }

            if (AtlasFontRegistry.TryResolve(face, _style.FontPixels(baseSize), out byte slot))
            {
                return (slot, true);
            }

            return profile.ResolveFont(FontLocation.StatusBars, 1, true);
        }

        private static Vector3 HueRgb(ushort hue)
        {
            uint c = Client.Game.UO.FileManager.Hues.GetPolygoneColor(22, hue);

            return new Vector3((c & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, ((c >> 16) & 0xFF) / 255f);
        }

        private void MarkOutOfRange()
        {
            _outOfRange = true;
            _model.OutOfRange = true;

            for (int i = 0; i < 3; i++)
            {
                _model.Fraction[i] = 0f;
            }

            if (_textBox != null)
            {
                _textBox.Hue = DIM_HUE;
                _textBox.IsEditable = false;
            }
        }

        public override void Update()
        {
            base.Update();

            if (IsDisposed || _view == null)
            {
                return;
            }

            bool inparty = World.Party.Contains(LocalSerial) || World.PluginParty.Contains(LocalSerial);
            Entity entity = World.Get(LocalSerial);

            if (entity is Item it && it.Layer == 0 && it.Container == World.Player)
            {
                entity = null;
            }

            if (entity == null || entity.IsDestroyed)
            {
                if (
                    LocalSerial != World.Player
                    && (
                        ProfileManager.CurrentProfile.CloseHealthBarType == 1
                        || ProfileManager.CurrentProfile.CloseHealthBarType == 2
                            && World.CorpseManager.Exists(0, LocalSerial | 0x8000_0000)
                    )
                    && !inparty
                    && CheckIfAnchoredElseDispose()
                )
                {
                    return;
                }

                _isDead = false;

                if (!_outOfRange)
                {
                    if (World.TargetManager.LastAttack != LocalSerial)
                    {
                        GameActions.SendCloseStatus(World, LocalSerial);
                    }

                    MarkOutOfRange();
                }

                _model.Dead = false;
                UpdateTexts(null);
                EmitChangedIfNeeded(null);

                return;
            }

            Mobile mobile = entity as Mobile;

            if (!_isDead && entity != World.Player && mobile != null && mobile.IsDead && ProfileManager.CurrentProfile.CloseHealthBarType == 2)
            {
                if (!inparty && CheckIfAnchoredElseDispose())
                {
                    return;
                }
            }

            if (mobile != null && _canChangeName != mobile.IsRenamable)
            {
                _canChangeName = mobile.IsRenamable;

                if (_textBox != null)
                {
                    _textBox.AcceptMouseInput = _canChangeName;
                    _textBox.AcceptKeyboardInput = _canChangeName;

                    if (!_canChangeName)
                    {
                        _textBox.IsEditable = false;
                    }
                }
            }

            _isDead = mobile != null && mobile.IsDead;

            string resolved = World.AliasManager.Resolve(entity.Serial, entity.Name);

            if (!string.IsNullOrEmpty(entity.Name) && _name != resolved)
            {
                _name = resolved;
                _textBox?.SetText(_name);
            }

            if (_outOfRange)
            {
                if (entity.HitsMax == 0)
                {
                    GameActions.RequestMobileStatus(World, entity);
                }

                _outOfRange = false;
                _model.OutOfRange = false;
                _canChangeName = mobile != null && mobile.IsRenamable;
            }

            ushort nameHue = mobile != null ? Notoriety.GetHue(mobile.NotorietyFlag) : DIM_HUE;
            ushort accentHue = nameHue;

            if (mobile != null && mobile != World.Player && World.StatusbarColorManager.TryGetColor(mobile.Graphic, mobile.Hue, out ushort sbcColor))
            {
                accentHue = sbcColor;
            }

            if (_textBox != null && _textBox.Hue != (_canChangeName ? (ushort)0x000E : nameHue))
            {
                _textBox.Hue = _canChangeName ? (ushort)0x000E : nameHue;
            }

            _model.Notoriety = HueRgb(accentHue);
            _model.Dead = _isDead;
            _model.Target = IsCurrentTarget();
            _model.Alert = _model.Target || (LocalSerial == World.Player && World.Player.InWarMode);

            ushort ringHue = PluginStatusOverlays.Get(LocalSerial);
            ushort backgroundHue = PluginStatusOverlays.GetBackground(LocalSerial);
            _model.HasRingOverride = ringHue != 0;
            _model.RingOverride = ringHue != 0 ? HueRgb(ringHue) : Vector3.Zero;
            _model.HasBackgroundOverride = backgroundHue != 0;
            _model.BackgroundOverride = backgroundHue != 0 ? HueRgb(backgroundHue) : Vector3.Zero;

            // HP row
            Vector3 hpColor = _style.Hp;

            if (mobile != null && mobile.IsPoisoned)
            {
                hpColor = _style.Poison;
            }
            else if (mobile != null && mobile.IsYellowHits)
            {
                hpColor = _style.Mortal;
            }

            _model.FillColor[0] = hpColor;
            _model.Fraction[0] = entity.HitsMax > 0 ? Math.Clamp(entity.Hits / (float)entity.HitsMax, 0f, 1f) : 0f;

            if (_rows == 3)
            {
                _model.FillColor[1] = _style.Mana;
                _model.FillColor[2] = _style.Stamina;
                bool vitals = mobile != null && (inparty || LocalSerial == World.Player);
                _model.Fraction[1] = vitals && mobile.ManaMax > 0 ? Math.Clamp(mobile.Mana / (float)mobile.ManaMax, 0f, 1f) : 0f;
                _model.Fraction[2] = vitals && mobile.StaminaMax > 0 ? Math.Clamp(mobile.Stamina / (float)mobile.StaminaMax, 0f, 1f) : 0f;
            }

            if (UIManager.MouseOverControl != null && UIManager.MouseOverControl.RootParent == this)
            {
                SelectedObject.HealthbarObject = entity;
                SelectedObject.Object = entity;
            }

            UpdateTexts(entity);
            EmitChangedIfNeeded(entity);
        }

        private bool IsCurrentTarget()
        {
            return LocalSerial != 0
                && LocalSerial != World.Player
                && (
                    World.TargetManager.LastTargetInfo.Serial == LocalSerial
                    || World.TargetManager.LastAttack == LocalSerial
                    || World.TargetManager.SelectedTarget == LocalSerial
                    || World.TargetManager.NewTargetSystemSerial == LocalSerial
                );
        }

        // Rebuilds a value label only when its numbers change, and keeps it aligned to its row.
        private void UpdateTexts(Entity entity)
        {
            Mobile mobile = entity as Mobile;
            bool show = entity != null && !_outOfRange;

            SetValue(_hpText, 0, show, entity?.Hits ?? 0, entity?.HitsMax ?? 0, ref _tHits, ref _tHitsMax);
            SetValue(_manaText, 1, show && mobile != null, mobile?.Mana ?? 0, mobile?.ManaMax ?? 0, ref _tMana, ref _tManaMax);
            SetValue(_stamText, 2, show && mobile != null, mobile?.Stamina ?? 0, mobile?.StaminaMax ?? 0, ref _tStam, ref _tStamMax);

            if (_distanceText != null)
            {
                int distance = entity != null && !entity.IsDestroyed ? entity.Distance : 0;

                _distanceText.IsVisible = show;

                if (distance != _tDistance)
                {
                    _tDistance = distance;
                    _distanceText.Text = distance.ToString();
                    int zone = _style.S(ModernBarStyle.DistanceZoneWidth);
                    _distanceText.X = Width - zone + (zone - _distanceText.Width) / 2;
                    _distanceText.Y = _style.S(ModernBarStyle.HeaderHeight) + _style.S(ModernBarStyle.BodyPadding)
                        + (_rows * _style.S(ModernBarStyle.RowHeight) - _distanceText.Height) / 2
                        + _style.TextNudge(ModernBarStyle.NameFontSize);
                }
            }
        }

        private void SetValue(Label label, int row, bool visible, int cur, int max, ref int cachedCur, ref int cachedMax)
        {
            if (label == null)
            {
                return;
            }

            label.IsVisible = visible;

            if (!visible || (cur == cachedCur && max == cachedMax))
            {
                return;
            }

            cachedCur = cur;
            cachedMax = max;
            label.Text = $"{cur}/{max}";

            // centered on the bar
            label.X = _model.BarX + (_model.BarWidth - label.Width) / 2;
            label.Y = _model.RowCenter(row) - label.Height / 2 + _style.TextNudge(ModernBarStyle.ValueFontSize);
        }

        /// <summary>Everything the view needs to draw one frame; written by the gump, read by the view.</summary>
        private sealed class BarModel
        {
            public ModernBarStyle Style;
            public int Rows, Width, Height;
            public bool ShowDistance;
            public readonly float[] Fraction = new float[3];
            public readonly Vector3[] FillColor = new Vector3[3];
            public Vector3 Notoriety;
            public bool OutOfRange, Dead, Target, Alert;
            public bool HasRingOverride, HasBackgroundOverride;
            public Vector3 RingOverride, BackgroundOverride;

            public void Reset(ModernBarStyle style, int rows, bool showDistance, int width, int height)
            {
                Style = style;
                Rows = rows;
                ShowDistance = showDistance;
                Width = width;
                Height = height;
                FillColor[0] = style.Hp;
                FillColor[1] = style.Mana;
                FillColor[2] = style.Stamina;
                OutOfRange = Dead = Target = Alert = HasRingOverride = HasBackgroundOverride = false;
            }

            public int BarX => Style.S(ModernBarStyle.Padding);

            public int BarWidth => Math.Max(
                Style.S(ModernBarStyle.BarHeight),
                Width - (ShowDistance ? Style.S(ModernBarStyle.DistanceZoneWidth) : 0) - Style.S(ModernBarStyle.Padding) - BarX
            );

            public int RowCenter(int row)
            {
                return Style.S(ModernBarStyle.HeaderHeight) + Style.S(ModernBarStyle.BodyPadding)
                    + row * Style.S(ModernBarStyle.RowHeight) + Style.S(ModernBarStyle.RowHeight) / 2;
            }
        }

        private sealed class ModernBarView : Control
        {
            private static readonly Vector3 Gray = new Vector3(0.45f, 0.45f, 0.48f);
            private static readonly Microsoft.Xna.Framework.Graphics.Texture2D White = SolidColorTextureCache.GetTexture(Color.White);

            private readonly BarModel _m;

            public ModernBarView(BarModel model)
            {
                _m = model;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                float depth = layerDepthRef;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    Draw(batcher, x, y, depth);

                    return true;
                });

                return true;
            }

            private void Draw(UltimaBatcher2D b, int x, int y, float depth)
            {
                BarModel m = _m;
                ModernBarStyle st = m.Style;
                int w = m.Width, h = m.Height;
                int r = st.S(ModernBarStyle.Radius);
                int bt = st.S(ModernBarStyle.BorderThickness);
                int headerH = st.S(ModernBarStyle.HeaderHeight);
                float opacity = st.BackgroundOpacity;

                bool dim = m.Dead || m.OutOfRange;
                Vector3 accent = dim ? Gray : m.Notoriety;
                Vector3 border = st.NotorietyAccent ? accent : (dim ? Gray : st.Border);

                if (m.HasRingOverride)
                {
                    border = m.RingOverride;
                }
                else if (m.Alert && !dim)
                {
                    border = st.Alert;
                }

                Vector3 bodyColor = st.NotorietyAccent ? ModernBarStyle.Mix(st.Background, accent, 0.08f) : st.Background;
                Vector3 headerColor = st.NotorietyAccent ? ModernBarStyle.Mix(st.Background, accent, 0.28f) : (dim ? Gray * 0.4f : st.Header);

                if (m.HasBackgroundOverride)
                {
                    bodyColor = ModernBarStyle.Mix(bodyColor, m.BackgroundOverride, 0.5f);
                    headerColor = ModernBarStyle.Mix(headerColor, m.BackgroundOverride, 0.5f);
                }

                var bounds = new Rectangle(x, y, w, h);

                RoundedRect.Fill(b, bounds, r, bodyColor, opacity, depth);
                RoundedRect.Fill(b, new Rectangle(x, y, w, headerH), r, headerColor, opacity, depth);
                // square off the header's lower corners so only the top follows the frame radius
                b.DrawTinted(White, new Rectangle(x, y + headerH - r, w, r), new Rectangle(0, 0, 1, 1), headerColor, opacity, depth);
                b.DrawTinted(White, new Rectangle(x + bt, y + headerH, w - bt * 2, Math.Max(1, st.S(1))), new Rectangle(0, 0, 1, 1), border, 0.55f, depth);

                int pad = st.S(ModernBarStyle.Padding);
                int barH = st.S(ModernBarStyle.BarHeight);
                int barX = m.BarX;
                int barW = m.BarWidth;

                for (int row = 0; row < m.Rows; row++)
                {
                    int cy = y + m.RowCenter(row);
                    Vector3 fill = dim ? Gray : m.FillColor[row];
                    var track = new Rectangle(x + barX, cy - barH / 2, barW, barH);

                    RoundedRect.Fill(b, track, barH / 2, st.Track, 0.95f, depth);

                    int fillW = ModernBarStyle.FillWidth(m.Fraction[row], barW, barH);

                    if (fillW > 0)
                    {
                        RoundedRect.Fill(b, new Rectangle(track.X, track.Y, fillW, barH), barH / 2, fill, 1f, depth);

                        if (fillW > barH)
                        {
                            int glossH = Math.Max(2, barH * 4 / 10);
                            RoundedRect.Fill(b, new Rectangle(track.X + barH / 3, track.Y + Math.Max(1, barH / 8), fillW - barH * 2 / 3, glossH), glossH / 2, Vector3.One, 0.18f, depth);
                        }
                    }
                }

                if (m.ShowDistance)
                {
                    int dividerX = x + w - st.S(ModernBarStyle.DistanceZoneWidth);
                    int top = y + headerH + st.S(ModernBarStyle.BodyPadding);
                    b.DrawTinted(White, new Rectangle(dividerX, top, Math.Max(1, st.S(1)), m.Rows * st.S(ModernBarStyle.RowHeight)), new Rectangle(0, 0, 1, 1), border, 0.45f, depth);
                }

                RoundedRect.Ring(b, bounds, r, bt, border, 1f, depth);

                if (m.Target)
                {
                    int arrow = st.S(ModernBarStyle.TargetArrowSize);
                    int ay = y + (h - arrow) / 2;
                    const int overlap = 3; // px the arrow tips reach into the panel
                    int outline = Math.Max(1, st.S(1));

                    // grey at the outer edge brightening to white toward the panel
                    Vector3 far = new Vector3(0.50f), near = new Vector3(0.96f);
                    Vector3 edge = new Vector3(0.06f);

                    RoundedRect.GlyphOutlinedGradient(b, StatGlyph.ArrowRight, new Rectangle(x + overlap - (int)MathF.Round(arrow * 0.90f), ay, arrow, arrow), far, near, edge, outline, depth);
                    RoundedRect.GlyphOutlinedGradient(b, StatGlyph.ArrowLeft, new Rectangle(x + w - overlap - (int)MathF.Round(arrow * 0.10f), ay, arrow, arrow), near, far, edge, outline, depth);
                }
            }
        }
    }
}
