// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Scenes;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    internal sealed class AlphaBlendControl : Control
    {
        public AlphaBlendControl(float alpha = 0.5f)
        {
            Alpha = alpha;
            AcceptMouseInput = false;
        }

        public ushort Hue { get; set; }

        // Solid color the alpha-blended fill is drawn from. Defaults to black (the historical
        // behaviour). Set to a light color for a pale panel — hueing the black texture can't
        // lighten it, so the base color itself must change.
        public Color BaseColor { get; set; } = Color.Black;

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float layerDepth = layerDepthRef;
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(Hue, false, Alpha);

            renderLists.AddGumpNoAtlas
            (
                batcher =>
                {
                    batcher.Draw
                    (
                        SolidColorTextureCache.GetTexture(BaseColor),
                        new Rectangle
                        (
                            x,
                            y,
                            Width,
                            Height
                        ),
                        hueVector,
                        layerDepth
                    );

                    return true;
                }
            );

            return true;
        }
    }
}