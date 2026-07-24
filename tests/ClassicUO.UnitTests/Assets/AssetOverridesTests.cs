// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class AssetOverridesTests
    {
        [Theory]
        [InlineData("1234", true, 1234u)]
        [InlineData("0", true, 0u)]
        [InlineData("0x04D2", true, 1234u)]
        [InlineData("0X04d2", true, 1234u)]
        [InlineData("", false, 0u)]
        [InlineData("abc", false, 0u)]
        [InlineData("12.3", false, 0u)]
        public void TryParseId_ParsesDecimalAndHex(string stem, bool ok, uint expected)
        {
            bool result = AssetOverrides.TryParseId(stem, out uint id);
            Assert.Equal(ok, result);
            if (ok)
                Assert.Equal(expected, id);
        }

        // 2x2 RGBA PNG: red, green / blue, transparent
        private const string Png2x2 =
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1B" +
            "AACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAUSURBVBhXY/jPwPAfDBkY/oMIBgA/" +
            "0gX7juj8nQAAAABJRU5ErkJggg==";

        [Fact]
        public void TryDecodePng_ReturnsPackedRgbaWithRLowByte()
        {
            byte[] bytes = System.Convert.FromBase64String(Png2x2);

            bool ok = AssetOverrides.TryDecodePng(bytes, out uint[] px, out int w, out int h);

            Assert.True(ok);
            Assert.Equal(2, w);
            Assert.Equal(2, h);
            Assert.Equal(4, px.Length);
            Assert.Equal(0xFF_00_00_FFu, px[0]); // red opaque  (a=FF,b=00,g=00,r=FF)
            Assert.Equal(0xFF_00_FF_00u, px[1]); // green opaque (a=FF,b=00,g=FF,r=00)
            Assert.Equal(0xFF_FF_00_00u, px[2]); // blue opaque  (a=FF,b=FF,g=00,r=00)
            Assert.Equal(0x00_00_00_00u, px[3]); // transparent
        }

        [Fact]
        public void TryDecodePng_RejectsGarbage()
        {
            Assert.False(AssetOverrides.TryDecodePng(new byte[] { 1, 2, 3 }, out _, out _, out _));
            Assert.False(AssetOverrides.TryDecodePng(null, out _, out _, out _));
        }

        // 22050Hz 16-bit mono PCM WAV, data = 0x11,0x11,0x22,0x22
        private const string Wav22050Mono16 =
            "UklGRigAAABXQVZFZm10IBAAAAABAAEAIlYAAESsAAACABAAZGF0YQQAAAARESIi";

        [Fact]
        public void TryParseSidecarCenter_ReadsCenter()
        {
            Assert.True(AssetOverrides.TryParseSidecarCenter(
                "{ \"centerX\": 12, \"centerY\": 40 }", out int cx, out int cy));
            Assert.Equal(12, cx);
            Assert.Equal(40, cy);
        }

        [Fact]
        public void TryParseSidecarCenter_RejectsMalformed()
        {
            Assert.False(AssetOverrides.TryParseSidecarCenter("not json", out _, out _));
            Assert.False(AssetOverrides.TryParseSidecarCenter("{}", out _, out _));
        }

        [Fact]
        public void TryExtractWavPcm_ReturnsDataChunk()
        {
            byte[] bytes = System.Convert.FromBase64String(Wav22050Mono16);
            Assert.True(AssetOverrides.TryExtractWavPcm(bytes, out byte[] pcm));
            Assert.Equal(new byte[] { 0x11, 0x11, 0x22, 0x22 }, pcm);
        }

        [Fact]
        public void TryExtractWavPcm_RejectsGarbage()
        {
            Assert.False(AssetOverrides.TryExtractWavPcm(new byte[] { 1, 2, 3, 4 }, out _));
        }

        private static string WriteTempTree()
        {
            byte[] png = System.Convert.FromBase64String(Png2x2);
            byte[] wav = System.Convert.FromBase64String(Wav22050Mono16);

            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cuo_ovr_" + System.Guid.NewGuid().ToString("N"));

            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Gump"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Art"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Anim", "400"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Sound"));

            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Gump", "1234.png"), png);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Gump", "bogus.png"), png); // skipped
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Art", "0x10.png"), png);    // id 16
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Art", "16.json"),
                System.Text.Encoding.UTF8.GetBytes("{ \"centerX\": 3, \"centerY\": 7 }"));
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Anim", "400", "1_2_3.png"), png);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, "Sound", "55.wav"), wav);

            return root;
        }

        [Fact]
        public void Build_IndexesAndServesOverrides()
        {
            string root = WriteTempTree();
            try
            {
                var ovr = new AssetOverrides();
                ovr.Build(root);
                Assert.True(ovr.Enabled);

                // Gump: decimal id, no sidecar
                Assert.True(ovr.TryGetImage(AssetOverrideKind.Gump, 1234,
                    out uint[] gpx, out int gw, out int gh, out bool gHasC, out _, out _));
                Assert.Equal(4, gpx.Length);
                Assert.Equal(2, gw);
                Assert.Equal(2, gh);
                Assert.False(gHasC);

                // bogus filename was skipped, missing id returns false
                Assert.False(ovr.TryGetImage(AssetOverrideKind.Gump, 9999,
                    out _, out _, out _, out _, out _, out _));

                // Art: hex id + sidecar center
                Assert.True(ovr.TryGetImage(AssetOverrideKind.Art, 16,
                    out _, out _, out _, out bool aHasC, out int acx, out int acy));
                Assert.True(aHasC);
                Assert.Equal(3, acx);
                Assert.Equal(7, acy);

                // Animation frame
                Assert.True(ovr.TryGetAnimationFrame(400, 1, 2, 3,
                    out uint[] fpx, out _, out _, out _, out _, out _));
                Assert.Equal(4, fpx.Length);
                Assert.False(ovr.TryGetAnimationFrame(400, 9, 9, 9,
                    out _, out _, out _, out _, out _, out _));

                // Sound
                Assert.True(ovr.TryGetSound(55, out byte[] pcm));
                Assert.Equal(new byte[] { 0x11, 0x11, 0x22, 0x22 }, pcm);
            }
            finally
            {
                System.IO.Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Build_MissingRoot_Disabled()
        {
            var ovr = new AssetOverrides();
            ovr.Build(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cuo_ovr_missing_" + System.Guid.NewGuid().ToString("N")));
            Assert.False(ovr.Enabled);
            Assert.False(ovr.TryGetImage(AssetOverrideKind.Gump, 1, out _, out _, out _, out _, out _, out _));
        }
    }
}
