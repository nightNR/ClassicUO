// SPDX-License-Identifier: BSD-2-Clause

using System;
using Microsoft.Xna.Framework;

namespace ClassicUO.Renderer.LoginFonts
{
    public interface ILoginFont
    {
        Vector2 Measure(ReadOnlySpan<char> text, float size, float letterSpacing = 0f);

        void Draw(
            UltimaBatcher2D batcher,
            ReadOnlySpan<char> text,
            float x,
            float y,
            float size,
            Color color,
            float opacity = 1f,
            float letterSpacing = 0f);
    }
}
