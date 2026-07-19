// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    // Reusable ornate frame built from the gothic border slices. Corners are drawn
    // at a uniform `scale` of native size; the top/bottom bars and the left/right
    // borders are TILED (repeated) at that scale to fill the span — never stretched.
    // A single side slice is used for every edge, rotated for the horizontal bars,
    // so all four edges share identical scaling/orientation. Textures are mipmapped
    // (see LoginAssets) and sampled with AnisotropicClamp so the heavily-minified
    // high-res art stays smooth and keeps its thin gold highlight lines (plain
    // point/bilinear drops them and adds grain). Per-corner and per-ornament pixel
    // nudges allow fine alignment.
    internal class NineSliceFrame : Control
    {
        private readonly float _scale;
        private readonly bool _withOrnaments;
        private readonly int _cornerOffsetX;
        private readonly int _cornerOffsetY;
        private readonly Point _dTL, _dTR, _dBL, _dBR;
        private readonly int _ornTopDeltaY, _ornBotDeltaY;
        private readonly int _fillOut;

        public NineSliceFrame(int x, int y, int w, int h, float scale = 0.55f, bool withOrnaments = true,
            int cornerOffsetX = 0, int cornerOffsetY = 0,
            Point dTL = default, Point dTR = default, Point dBL = default, Point dBR = default,
            int ornTopDeltaY = 0, int ornBotDeltaY = 0, int fillOut = 0)
        {
            _fillOut = fillOut;
            X = x;
            Y = y;
            Width = w;
            Height = h;
            _scale = scale;
            _withOrnaments = withOrnaments;
            _cornerOffsetX = cornerOffsetX;
            _cornerOffsetY = cornerOffsetY;
            _dTL = dTL; _dTR = dTR; _dBL = dBL; _dBR = dBR;
            _ornTopDeltaY = ornTopDeltaY;
            _ornBotDeltaY = ornBotDeltaY;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float depth = layerDepthRef;
            float s = _scale;
            int cox = _cornerOffsetX, coy = _cornerOffsetY;
            Point dTL = _dTL, dTR = _dTR, dBL = _dBL, dBR = _dBR;
            int ornTopDY = _ornTopDeltaY, ornBotDY = _ornBotDeltaY;
            bool withOrnaments = _withOrnaments;
            Vector3 hue = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);

            Texture2D tl = LoginAssets.CornerTL, tr = LoginAssets.CornerTR;
            Texture2D bl = LoginAssets.CornerBL, br = LoginAssets.CornerBR;
            Texture2D sl = LoginAssets.SideL;
            Texture2D ctr = LoginAssets.Center;
            Texture2D topOrn = LoginAssets.TopOrn, botOrn = LoginAssets.BottomOrn;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                Rectangle Full(Texture2D t) => new Rectangle(0, 0, t.Width, t.Height);

                batcher.SetSampler(SamplerState.AnisotropicClamp);

                // Repeat `tex` vertically across [y0, y1) at a scaled tile height.
                void VTile(Texture2D tex, int tx, int y0, int y1, int bandW, int tileH)
                {
                    if (y1 <= y0 || tileH <= 0) return;
                    for (int py = y0; py < y1; py += tileH)
                    {
                        int h = Math.Min(tileH, y1 - py);
                        int srcH = Math.Max(1, (int)(tex.Height * (h / (float)tileH)));
                        batcher.Draw(tex, new Rectangle(tx, py, bandW, h), new Rectangle(0, 0, tex.Width, srcH), hue, depth);
                    }
                }

                // Horizontal bar from the side slice rotated a consistent +90° and
                // tiled left→right across [xL, xR). Same orientation everywhere, so
                // both sides of the ornament match (no mirrored/odd edge). Band
                // thickness = side width * s (uniform with the vertical borders).
                void HRotBar(Texture2D tex, int xL, int xR, int y0, float sc)
                {
                    int tileLen = (int)(tex.Height * sc);
                    if (tileLen <= 0 || xR <= xL) return;
                    for (int px = xL; px < xR; px += tileLen)
                    {
                        int right = Math.Min(px + tileLen, xR);
                        int len = right - px;
                        int srcH = Math.Max(1, (int)(tex.Height * (len / (float)tileLen)));
                        // +90° anchored at the tile's right edge, extending left by len,
                        // band running down from y0.
                        batcher.Draw(tex, new Vector2(right, y0), new Rectangle(0, 0, tex.Width, srcH),
                            hue, MathHelper.PiOver2, Vector2.Zero, new Vector2(sc, sc), SpriteEffects.None, depth);
                    }
                }

                int cwL = (int)(tl.Width * s), cwR = (int)(tr.Width * s);
                int chT = (int)(tl.Height * s), chB = (int)(bl.Height * s);
                int sideBand = (int)(sl.Width * s);   // uniform border thickness (all four edges)
                int cx = x + Width / 2;
                int botY = y + Height - sideBand;

                // center fill (dark panel behind everything) — extended outward by
                // half the border thickness so it tucks UNDER the outer half of the
                // borders; nothing shows through even where the border art is
                // semi-transparent near its edge. Bars/corners draw on top.
                int fillOut = _fillOut;
                batcher.Draw(ctr, new Rectangle(x - fillOut, y - fillOut, Width + 2 * fillOut, Height + 2 * fillOut), Full(ctr), hue, depth);

                // top/bottom bars — full width, consistent orientation; corners on top
                // hide the overlap so bars always reach the corners with no gap.
                HRotBar(sl, x, x + Width, y, s);
                HRotBar(sl, x, x + Width, botY, s);

                // left/right borders — same slice, identical scaling on both sides.
                VTile(sl, x, y, y + Height, sideBand, (int)(sl.Height * s));
                VTile(sl, x + Width - sideBand, y, y + Height, sideBand, (int)(sl.Height * s));

                // corners (scaled, base outward offset + per-corner pixel nudge)
                batcher.Draw(tl, new Rectangle(x - cox + dTL.X, y - coy + dTL.Y, cwL, chT), Full(tl), hue, depth);
                batcher.Draw(tr, new Rectangle(x + Width - cwR + cox + dTR.X, y - coy + dTR.Y, cwR, chT), Full(tr), hue, depth);
                batcher.Draw(bl, new Rectangle(x - cox + dBL.X, y + Height - chB + coy + dBL.Y, cwL, chB), Full(bl), hue, depth);
                batcher.Draw(br, new Rectangle(x + Width - cwR + cox + dBR.X, y + Height - chB + coy + dBR.Y, cwR, chB), Full(br), hue, depth);

                // center ornaments — overlay over the middle of each bar (base outward
                // nudge of 5px, plus a per-ornament delta).
                if (withOrnaments)
                {
                    const int ornOut = 5;
                    int topOrnW = (int)(topOrn.Width * s), topOrnH = (int)(topOrn.Height * s);
                    batcher.Draw(topOrn, new Rectangle(cx - topOrnW / 2, y + sideBand / 2 - topOrnH / 2 - ornOut + ornTopDY, topOrnW, topOrnH), Full(topOrn), hue, depth);

                    int botOrnW = (int)(botOrn.Width * s), botOrnH = (int)(botOrn.Height * s);
                    batcher.Draw(botOrn, new Rectangle(cx - botOrnW / 2, botY + sideBand / 2 - botOrnH / 2 + ornOut + ornBotDY, botOrnW, botOrnH), Full(botOrn), hue, depth);
                }

                batcher.SetSampler(SamplerState.PointClamp);
                return true;
            });

            return true;
        }
    }
}
#endif
