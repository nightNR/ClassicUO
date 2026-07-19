// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using ClassicUO.Renderer.LoginFonts;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Renderer
{
    public class LoginFontMeasureTests
    {
        private sealed class FixedFont : ILoginFont
        {
            public Vector2 Measure(ReadOnlySpan<char> text, float size, float letterSpacing = 0f)
                => new Vector2(text.Length * size * 0.5f + Math.Max(0, text.Length - 1) * letterSpacing, size);

            public void Draw(UltimaBatcher2D batcher, ReadOnlySpan<char> text, float x, float y,
                float size, Color color, float opacity = 1f, float letterSpacing = 0f) { }
        }

        [Fact]
        public void Measure_ScalesWithLengthAndSpacing()
        {
            ILoginFont f = new FixedFont();
            var a = f.Measure("AB", 20f);
            var b = f.Measure("AB", 20f, letterSpacing: 4f);
            Assert.True(b.X > a.X);
            Assert.Equal(20f, a.Y);
        }
    }
}
