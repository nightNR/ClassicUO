// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Game.Scenes;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Controls
{
    internal class TextureImage : Control
    {
        private readonly Texture2D _texture;

        public TextureImage(Texture2D texture, int x, int y, int w, int h)
        {
            _texture = texture;
            X = x;
            Y = y;
            Width = w;
            Height = h;
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            float layerDepth = layerDepthRef;
            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0, false, Alpha, true);
            var dest = new Rectangle(x, y, Width, Height);
            var src = new Rectangle(0, 0, _texture.Width, _texture.Height);

            renderLists.AddGumpNoAtlas(batcher =>
            {
                batcher.SetSampler(SamplerState.AnisotropicClamp);
                batcher.Draw(_texture, dest, src, hueVector, layerDepth);
                batcher.SetSampler(SamplerState.PointClamp);
                return true;
            });

            return true;
        }
    }
}
#endif
