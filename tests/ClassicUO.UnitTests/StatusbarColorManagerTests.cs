// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.IO;
using System.Threading;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class StatusbarColorManagerTests
    {
        private static int _seq;

        private static StatusbarColorManager New()
        {
            var m = new StatusbarColorManager(null);
            m.XmlPathOverride = Path.Combine(Path.GetTempPath(), $"cuo_sbc_{Interlocked.Increment(ref _seq)}.xml");
            if (File.Exists(m.XmlPathOverride)) File.Delete(m.XmlPathOverride);
            return m;
        }

        private static StatusbarColorRule Rule(ushort g, ushort color, params ushort[] hues) =>
            new StatusbarColorRule { Graphic = g, Color = color, Hues = new List<ushort>(hues) };

        [Fact]
        public void TryGetColor_ExactGraphicAndHue()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044, 0x0022));
            Assert.True(m.TryGetColor(0x00C8, 0x0022, out var c));
            Assert.Equal((ushort)0x0044, c);
        }

        [Fact]
        public void TryGetColor_EmptyHueList_MatchesAnyHue()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044));            // no hues => any
            Assert.True(m.TryGetColor(0x00C8, 0x9999, out var c));
            Assert.Equal((ushort)0x0044, c);
        }

        [Fact]
        public void TryGetColor_GraphicMatchHueMiss_NoMatch()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044, 0x0022));
            Assert.False(m.TryGetColor(0x00C8, 0x0033, out _));
        }

        [Fact]
        public void TryGetColor_NoGraphicMatch_NoMatch()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044));
            Assert.False(m.TryGetColor(0x00C9, 0x0044, out _));
        }

        [Fact]
        public void TryGetColor_Disabled_ReturnsFalse()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0044));
            m.Enabled = false;
            Assert.False(m.TryGetColor(0x00C8, 0x0044, out _));
        }

        [Fact]
        public void TryGetColor_FirstMatchWins()
        {
            var m = New();
            m.Add(Rule(0x00C8, 0x0011));            // any hue
            m.Add(Rule(0x00C8, 0x0022, 0x0055));
            Assert.True(m.TryGetColor(0x00C8, 0x0055, out var c));
            Assert.Equal((ushort)0x0011, c);        // first rule wins
        }

        [Fact]
        public void Xml_RoundTrips_RulesIncludingMultiAndEmptyHues()
        {
            var path = Path.Combine(Path.GetTempPath(), $"cuo_sbc_rt_{Interlocked.Increment(ref _seq)}.xml");
            if (File.Exists(path)) File.Delete(path);

            var a = New();
            a.Add(Rule(0x00C8, 0x0044, 0x0022, 0x0033));
            a.Add(Rule(0x0190, 0x0055));            // empty hues
            a.SaveRules(path);

            var b = New();
            b.ReadRules(path);
            Assert.True(b.TryGetColor(0x00C8, 0x0033, out var c1));
            Assert.Equal((ushort)0x0044, c1);
            Assert.True(b.TryGetColor(0x0190, 0x1234, out var c2)); // empty => any
            Assert.Equal((ushort)0x0055, c2);

            File.Delete(path);
        }

        [Theory]
        [InlineData("0x44|0x22", new ushort[] { 0x44, 0x22 })]
        [InlineData("68|34", new ushort[] { 68, 34 })]
        [InlineData("", new ushort[] { })]
        [InlineData("  0x44 | 0x22 ", new ushort[] { 0x44, 0x22 })]
        public void ParseHues_Works(string input, ushort[] expected)
        {
            Assert.Equal(expected, StatusbarColorManager.ParseHues(input).ToArray());
        }
    }
}
