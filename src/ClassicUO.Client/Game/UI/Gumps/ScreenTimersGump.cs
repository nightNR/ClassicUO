// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Assets;
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

                StackDirection dir = StackDirection.Down;
                TimerGroup group = default;
                if (e.GroupId != 0 && ScreenTimers.TryGetGroup(e.GroupId, out group))
                {
                    dir = group.Direction;
                }

                int extent = ScreenTimers.DefaultExtent(e.Shape, dir, e.Width, e.Height);
                var (px, py) = ScreenTimers.ComputePosition(e, group, extent);
                float remaining = ScreenTimers.RemainingFraction(e, now);

                DrawEntry(renderLists, in e, px, py, remaining, depth, now);
            }

            PruneStaleTexts();

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
                    // Approximate radial depletion with a shrinking, centered filled
                    // square. A true arc can replace this later without touching any
                    // of the layout inputs (px, py, w, h, remaining) computed above.
                    int side = (int)(w * remaining);
                    int cx = px + (w - side) / 2;
                    int cy = py + (h - side) / 2;
                    if (side > 0)
                    {
                        renderLists.AddGumpNoAtlas(batcher =>
                        {
                            batcher.Draw(_fillTexture, new Rectangle(cx, cy, side, side), hued, depth);
                            return true;
                        });
                    }
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
