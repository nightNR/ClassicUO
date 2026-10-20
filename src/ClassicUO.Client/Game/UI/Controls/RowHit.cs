// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Scenes;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    /// <summary>List row highlight: always drawn when selected, lighter tint while hovered (plain HitBox only draws on hover).</summary>
    internal sealed class RowHit : HitBox
    {
        private static readonly Texture2D SelTexture = SolidColorTextureCache.GetTexture(new Color(70, 120, 200));
        private readonly bool _selected;

        public RowHit(int x, int y, int w, int h, string tooltip, bool selected) : base(x, y, w, h, tooltip, 0.15f)
        {
            _selected = selected;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            if (IsDisposed)
            {
                return false;
            }

            if (_selected || MouseIsOver)
            {
                float layerDepth = layerDepthRef;
                Texture2D tex = _selected ? SelTexture : _texture;
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, _selected ? (MouseIsOver ? 0.55f : 0.4f) : Alpha, true);
                int w = Width, h = Height;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    batcher.Draw(tex, new Vector2(x, y), new Rectangle(0, 0, w, h), hueVector, layerDepth);

                    return true;
                });
            }

            // no children; HitBox's own hover draw is intentionally skipped
            return true;
        }
    }
}
