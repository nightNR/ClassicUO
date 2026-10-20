// SPDX-License-Identifier: BSD-2-Clause
using ClassicUO.Resources;
using Xunit;

namespace ClassicUO.UnitTests.Resources
{
    public class FontFaceCatalogTests
    {
        [Fact]
        public void Faces_ContainsEightKnownFaces()
        {
            Assert.Equal(8, FontFaceCatalog.Faces.Count);
            Assert.Contains(FontFaceCatalog.Faces, f => f.Id == "interbold");
            Assert.Contains(FontFaceCatalog.Faces, f => f.Id == "inter");
            Assert.Contains(FontFaceCatalog.Faces, f => f.Id == "cinzel");
        }

        [Fact]
        public void TryGet_KnownFace_ReturnsIt()
        {
            Assert.True(FontFaceCatalog.TryGet("inter", out var face));
            Assert.Equal("Inter", face.DisplayName);
        }

        [Fact]
        public void TryGet_UnknownFace_False()
        {
            Assert.False(FontFaceCatalog.TryGet("nope", out _));
        }

        [Fact]
        public void GetBytes_KnownFace_ReturnsNonEmpty()
        {
            byte[] bytes = FontFaceCatalog.GetBytes("inter");
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 0);
        }
    }
}
