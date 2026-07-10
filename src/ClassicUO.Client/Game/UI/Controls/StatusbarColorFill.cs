// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Scenes;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class StatusbarColorFill : Control
    {
        public StatusbarColorFill()
        {
            AcceptMouseInput = false;
        }

        public ushort ColorHue { get; set; }
        public float FillAlpha { get; set; } = 0.8f;
        public int BorderSize { get; set; } = 1;

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float layerDepth = layerDepthRef;

            int w = Width, h = Height, b = BorderSize;
            ushort hue = ColorHue;
            float fillAlpha = FillAlpha;

            renderLists.AddGumpNoAtlas(
                batcher =>
                {
                    var tex = SolidColorTextureCache.GetTexture(Color.White);
                    Vector3 fill = ShaderHueTranslator.GetHueVector(hue, false, fillAlpha);
                    Vector3 border = ShaderHueTranslator.GetHueVector(hue, false, 1f);

                    batcher.Draw(tex, new Rectangle(x, y, w, h), fill, layerDepth);              // fill
                    batcher.Draw(tex, new Rectangle(x, y, w, b), border, layerDepth);            // top
                    batcher.Draw(tex, new Rectangle(x, y + h - b, w, b), border, layerDepth);    // bottom
                    batcher.Draw(tex, new Rectangle(x, y, b, h), border, layerDepth);            // left
                    batcher.Draw(tex, new Rectangle(x + w - b, y, b, h), border, layerDepth);    // right
                    return true;
                }
            );

            return true;
        }
    }
}
