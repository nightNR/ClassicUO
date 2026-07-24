using ClassicUO.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer.Lights
{
    public sealed class Light
    {
        private readonly TextureAtlas _atlas;
        private readonly SpriteInfo[] _spriteInfos;
        private readonly LightsLoader _lightsLoader;
        private readonly AssetOverrides _overrides;

        public Light(LightsLoader lightsLoader, AssetOverrides overrides, GraphicsDevice device)
        {
            _lightsLoader = lightsLoader;
            _overrides = overrides;
            _atlas = new TextureAtlas(device, 2048, 2048, SurfaceFormat.Color);
            _spriteInfos = new SpriteInfo[lightsLoader.File.Entries.Length];
        }

        public ref readonly SpriteInfo GetLight(uint idx)
        {
            if (idx >= _spriteInfos.Length)
                return ref SpriteInfo.Empty;

            ref var spriteInfo = ref _spriteInfos[idx];

            if (spriteInfo.Texture == null)
            {
                if (_overrides != null && _overrides.TryGetImage(
                        AssetOverrideKind.Light, idx,
                        out uint[] opx, out int ow, out int oh, out _, out _, out _))
                {
                    spriteInfo.Texture = _atlas.AddSprite(opx, ow, oh, out spriteInfo.UV);
                    return ref spriteInfo;
                }

                var lightInfo = _lightsLoader.GetLight(idx);
                if (!lightInfo.Pixels.IsEmpty)
                {
                    spriteInfo.Texture = _atlas.AddSprite(
                        lightInfo.Pixels,
                        lightInfo.Width,
                        lightInfo.Height,
                        out spriteInfo.UV
                    );
                }
            }

            return ref spriteInfo;
        }
    }
}
