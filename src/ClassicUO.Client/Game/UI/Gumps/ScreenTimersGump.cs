// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Assets;
using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Fixed-position, non-interactive overlay that renders every plugin screen
    /// timer each frame. One instance is added to <see cref="Managers.UIManager"/>
    /// at the top layer on game-scene load. All layout/position/remaining math
    /// lives in <see cref="ScreenTimers"/>; this class only turns entries into
    /// draw calls against the live batcher.
    /// </summary>
    internal sealed class ScreenTimersGump : Gump
    {
        // Solid-color source textures reused for every bar/circle fill. Tinting is
        // done at draw time via the hue vector, so one texture per role suffices.
        private static readonly Texture2D _bgTexture = SolidColorTextureCache.GetTexture(Color.Gray);
        private static readonly Texture2D _borderTexture = SolidColorTextureCache.GetTexture(Color.Black);
        private static readonly Texture2D _fillTexture = SolidColorTextureCache.GetTexture(Color.White);

        // Neutral (no recolor) hue vector for the gray backdrop / black border.
        private static readonly Vector3 _neutralHue = ShaderHueTranslator.GetHueVector(0, false, 1f, true);

        // Cached RenderedText per timer id. Like BuffControlEntry we build the glyph
        // layout once and only rebuild it when the displayed value actually changes,
        // instead of calling RenderedText.Create every frame (which would churn the
        // pool and re-lay-out fonts on the render path).
        private readonly Dictionary<int, TimerText> _texts = new Dictionary<int, TimerText>();
        private readonly List<int> _staleTextIds = new List<int>();

        public ScreenTimersGump(World world) : base(world, 0, 0)
        {
            CanMove = false;
            CanCloseWithRightClick = false;
            AcceptMouseInput = false;
            AcceptKeyboardInput = false;
            WantUpdateSize = false;
            X = 0;
            Y = 0;
            Width = 0;
            Height = 0;
        }

        // GumpType.None (the base default) keeps this transient overlay out of the
        // saved-gump set; it is re-added on every game-scene load instead.

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            if (IsDisposed)
            {
                return false;
            }

            long now = Time.Ticks;
            float depth = layerDepthRef;

            foreach (var kv in ScreenTimers.Entries)
            {
                var e = kv.Value;

                int px, py;

                if (e.AnchorKind != AnchorKind.None)
                {
                    if (!TryResolveAnchorScreen(in e, out px, out py))
                    {
                        continue; // anchor missing or off-screen: hide, keep counting
                    }
                }
                else
                {
                    StackDirection dir = StackDirection.Down;
                    TimerGroup group = default;
                    if (e.GroupId != 0 && ScreenTimers.TryGetGroup(e.GroupId, out group))
                    {
                        dir = group.Direction;
                    }

                    int extent = ScreenTimers.DefaultExtent(e.Shape, dir, e.Width, e.Height);
                    (px, py) = ScreenTimers.ComputePosition(e, group, extent);
                }

                float remaining = ScreenTimers.RemainingFraction(e, now);
                DrawEntry(renderLists, in e, px, py, remaining, depth, now);
            }

            PruneStaleTexts();

            return true;
        }

        // Resolves an anchored timer to a top-left screen pixel, or returns false
        // when the anchor entity is gone or the placement is outside the camera.
        // World-space pixel math mirrors NameOverheadGump / GameObject; the camera
        // transform + bounds cull mirror HealthLinesManager.
        private bool TryResolveAnchorScreen(in ScreenTimerEntry e, out int outX, out int outY)
        {
            outX = 0;
            outY = 0;

            int wx, wy;

            switch (e.AnchorKind)
            {
                case AnchorKind.Serial when SerialHelper.IsMobile(e.AnchorSerial):
                case AnchorKind.Self:
                {
                    Mobile m = e.AnchorKind == AnchorKind.Self
                        ? World.Player
                        : World.Mobiles.Get(e.AnchorSerial);
                    if (m == null)
                        return false;

                    Client.Game.UO.Animations.GetAnimationDimensions(
                        m.AnimIndex, m.GetGraphicForAnimation(), 0, 0, m.IsMounted, 0,
                        out int _, out int centerY, out int _, out int height);

                    wx = (int)(m.RealScreenPosition.X + m.Offset.X + 22);
                    wy = (int)(m.RealScreenPosition.Y + (m.Offset.Y - m.Offset.Z)
                             - (height + centerY + 8 + 22)
                             + (m.IsGargoyle && m.IsFlying ? -22 : !m.IsMounted ? 22 : 0));
                    break;
                }

                case AnchorKind.Serial: // item
                {
                    Item item = World.Items.Get(e.AnchorSerial);
                    if (item == null)
                        return false;

                    var bounds = Client.Game.UO.Arts.GetRealArtBounds(item.Graphic);
                    wx = item.RealScreenPosition.X + (int)item.Offset.X + 22;
                    wy = item.RealScreenPosition.Y + (int)(item.Offset.Y - item.Offset.Z)
                         - (bounds.Height >> 1);
                    break;
                }

                case AnchorKind.Absolute:
                {
                    // Derive the current draw offset from the player, whose world
                    // pixel and RealScreenPosition are both known this frame:
                    //   RealScreenPos = worldPixel - drawOffset - 22   (GameObject.cs:150-154)
                    Mobile player = World.Player;
                    if (player == null)
                        return false;

                    var (pwx, pwy) = ScreenTimers.TileToWorldPixel(player.X, player.Y, player.Z);
                    float offX = pwx - player.RealScreenPosition.X - 22;
                    float offY = pwy - player.RealScreenPosition.Y - 22;

                    var (twx, twy) = ScreenTimers.TileToWorldPixel(e.AnchorX, e.AnchorY, e.AnchorZ);
                    wx = (int)(twx - offX - 22);
                    wy = (int)(twy - offY - 22);
                    break;
                }

                default:
                    return false;
            }

            (int w, int h) = ScreenTimers.DefaultSize(e.Shape);
            if (e.Width > 0) w = e.Width;
            if (e.Height > 0) h = e.Height;

            var camera = Client.Game.Scene.Camera;
            Point p = camera.WorldToScreen(new Point(wx, wy));

            // Center horizontally on the anchor, sit above it, then apply nudge.
            int sx = p.X - (w >> 1) + e.AnchorOffsetX + camera.Bounds.X;
            int sy = p.Y + e.AnchorOffsetY + camera.Bounds.Y;

            if (sx < camera.Bounds.X || sx + w > camera.Bounds.Right)
                return false;
            if (sy < camera.Bounds.Y || sy + h > camera.Bounds.Bottom)
                return false;

            outX = sx;
            outY = sy;
            return true;
        }

        private void DrawEntry(RenderLists renderLists, in ScreenTimerEntry e, int px, int py, float remaining, float depth, long now)
        {
            (int w, int h) = ScreenTimers.DefaultSize(e.Shape);
            if (e.Width > 0) w = e.Width;
            if (e.Height > 0) h = e.Height;

            Vector3 hued = ShaderHueTranslator.GetHueVector(e.Hue, false, 1f, true);

            switch (e.Shape)
            {
                case TimerShape.Bar:
                {
                    int fill = (int)(w * remaining);
                    renderLists.AddGumpNoAtlas(batcher =>
                    {
                        batcher.Draw(_bgTexture, new Rectangle(px, py, w, h), _neutralHue, depth);
                        if (fill > 0)
                        {
                            batcher.Draw(_fillTexture, new Rectangle(px, py, fill, h), hued, depth);
                        }
                        batcher.DrawRectangle(_borderTexture, px, py, w, h, _neutralHue, depth);
                        return true;
                    });
                    break;
                }

                case TimerShape.Circle:
                {
                    // Ring outline that depletes clockwise from 12 o'clock. The gray
                    // backdrop draws the full circle; the hued arc covers only the
                    // remaining fraction, so it shrinks back toward 12 as time runs
                    // out. Built from short line segments since the batcher has no
                    // native arc primitive.
                    float radius = Math.Min(w, h) / 2f - 1f;
                    float ccx = px + w / 2f;
                    float ccy = py + h / 2f;
                    float frac = Math.Clamp(remaining, 0f, 1f);
                    renderLists.AddGumpNoAtlas(batcher =>
                    {
                        DrawArc(batcher, ccx, ccy, radius, 1f, _neutralHue, depth);
                        if (frac > 0f)
                        {
                            DrawArc(batcher, ccx, ccy, radius, frac, hued, depth);
                        }
                        return true;
                    });
                    break;
                }

                case TimerShape.Numeric:
                default:
                    break; // numeric is rendered purely as text below
            }

            if (e.ShowTime || e.Shape == TimerShape.Numeric || !string.IsNullOrEmpty(e.Label))
            {
                int secs = (int)Math.Ceiling(Math.Max(0L, e.StartTicks + e.DurationMs - now) / 1000f);
                bool wantTime = e.ShowTime || e.Shape == TimerShape.Numeric;

                RenderedText rt = GetOrBuildText(in e, secs, wantTime);
                if (rt != null && rt.Text.Length != 0)
                {
                    int textY = e.Shape == TimerShape.Numeric ? py : py + h + 1;
                    // Recolor at draw time; base glyphs are white so any hue shows.
                    renderLists.AddGumpNoAtlas(rt, px, textY, depth, 1f, e.Hue);
                }
            }
        }

        // Stroke width of the ring outline, in pixels.
        private const float CircleStroke = 2f;

        // Draws a clockwise arc starting at 12 o'clock and sweeping `frac` of a full
        // turn, approximated by line segments. Segment count scales with radius so
        // large timers stay smooth without over-drawing tiny ones.
        private static void DrawArc(UltimaBatcher2D batcher, float cx, float cy, float radius, float frac, Vector3 color, float depth)
        {
            if (radius <= 0f || frac <= 0f)
            {
                return;
            }

            const float TwoPi = MathF.PI * 2f;
            int segments = Math.Max(2, (int)(radius * frac));
            float sweep = frac * TwoPi;

            Vector2 prev = PointOnCircle(cx, cy, radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = sweep * (i / (float)segments);
                Vector2 cur = PointOnCircle(cx, cy, radius, t);
                batcher.DrawLine(_fillTexture, prev, cur, color, CircleStroke, depth);
                prev = cur;
            }
        }

        // Angle 0 = top (12 o'clock); increasing angle sweeps clockwise in screen
        // space (y grows downward).
        private static Vector2 PointOnCircle(float cx, float cy, float radius, float angle)
        {
            return new Vector2(cx + radius * MathF.Sin(angle), cy - radius * MathF.Cos(angle));
        }

        private RenderedText GetOrBuildText(in ScreenTimerEntry e, int secs, bool wantTime)
        {
            if (!_texts.TryGetValue(e.Id, out TimerText tt))
            {
                tt = new TimerText
                {
                    Rendered = RenderedText.Create(
                        string.Empty,
                        0xFFFF,
                        0xFF,
                        true,
                        FontStyle.BlackBorder,
                        TEXT_ALIGN_TYPE.TS_LEFT
                    ),
                };
                _texts[e.Id] = tt;
            }

            // Only rebuild the glyph layout when the visible value actually changes.
            if (tt.LastSecs != secs || tt.LastShowTime != wantTime || tt.LastLabel != e.Label)
            {
                tt.LastSecs = secs;
                tt.LastShowTime = wantTime;
                tt.LastLabel = e.Label;

                string text = e.Label ?? string.Empty;
                if (wantTime)
                {
                    text = text.Length == 0 ? secs + "s" : text + " " + secs + "s";
                }

                tt.Rendered.Text = text;
            }

            return tt.Rendered;
        }

        private void PruneStaleTexts()
        {
            if (_texts.Count == 0)
            {
                return;
            }

            _staleTextIds.Clear();
            foreach (var kv in _texts)
            {
                if (!ScreenTimers.Entries.ContainsKey(kv.Key))
                {
                    _staleTextIds.Add(kv.Key);
                }
            }

            for (int i = 0; i < _staleTextIds.Count; i++)
            {
                int id = _staleTextIds[i];
                if (_texts.TryGetValue(id, out TimerText tt))
                {
                    tt.Rendered?.Destroy();
                    _texts.Remove(id);
                }
            }
        }

        public override void Dispose()
        {
            foreach (var kv in _texts)
            {
                kv.Value.Rendered?.Destroy();
            }
            _texts.Clear();
            _staleTextIds.Clear();

            base.Dispose();
        }

        private sealed class TimerText
        {
            public RenderedText Rendered;
            public int LastSecs = int.MinValue;
            public bool LastShowTime;
            public string LastLabel;
        }
    }
}
