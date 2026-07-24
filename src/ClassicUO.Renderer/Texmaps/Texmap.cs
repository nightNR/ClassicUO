using ClassicUO.Assets;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer.Texmaps
{
    public sealed class Texmap
    {
        private readonly TextureAtlas _atlas;
        private readonly SpriteInfo[] _spriteInfos;
        private readonly PixelPicker _picker = new PixelPicker();
        private readonly TexmapsLoader _texmapsLoader;
        private readonly AssetOverrides _overrides;

        public Texmap(TexmapsLoader texmapsLoader, AssetOverrides overrides, GraphicsDevice device)
        {
            _texmapsLoader = texmapsLoader;
            _overrides = overrides;
            _atlas = new TextureAtlas(device, 2048, 2048, SurfaceFormat.Color);
            _spriteInfos = new SpriteInfo[texmapsLoader.File.Entries.Length];
        }

        public ref readonly SpriteInfo GetTexmap(uint idx)
        {
            if (idx >= _spriteInfos.Length)
                return ref SpriteInfo.Empty;

            ref var spriteInfo = ref _spriteInfos[idx];

            if (spriteInfo.Texture == null)
            {
                if (_overrides != null && _overrides.TryGetImage(
                        AssetOverrideKind.Texmap, idx,
                        out uint[] opx, out int ow, out int oh, out _, out _, out _))
                {
                    var otex = _atlas.AddSprite(opx, ow, oh, out var ouv);
                    if (otex != null)
                    {
                        spriteInfo.Texture = otex;
                        spriteInfo.UV = ouv;
                        _picker.Set(idx, ow, oh, opx);
                        return ref spriteInfo;
                    }
                }

                var texmapInfo = _texmapsLoader.GetTexmap(idx);
                if (!texmapInfo.Pixels.IsEmpty)
                {
                    spriteInfo.Texture = _atlas.AddSprite(
                        texmapInfo.Pixels,
                        texmapInfo.Width,
                        texmapInfo.Height,
                        out spriteInfo.UV
                    );

                    _picker.Set(idx, texmapInfo.Width, texmapInfo.Height, texmapInfo.Pixels);
                }
            }

            return ref spriteInfo;
        }
    }
}
