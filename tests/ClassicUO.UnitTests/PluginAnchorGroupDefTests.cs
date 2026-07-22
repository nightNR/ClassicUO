// SPDX-License-Identifier: BSD-2-Clause
using System.Text.Json;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests
{
    public class PluginAnchorGroupDefTests
    {
        [Fact]
        public void Defaults_AreSane()
        {
            var def = new PluginAnchorGroupDef();
            Assert.Equal("", def.Label);
            Assert.Equal(1, def.Columns);
            Assert.Equal(1, def.Rows);
            Assert.Equal(FillOrder.ColumnMajor, def.Fill);
        }

        [Fact]
        public void SerializationRoundTrip_PreservesAllFields()
        {
            var def = new PluginAnchorGroupDef
            {
                Id = 42, Label = "Enemies", Columns = 3, Rows = 5,
                Fill = FillOrder.RowMajor, X = 100, Y = 200, Locked = true
            };

            string json = JsonSerializer.Serialize(def);
            var round = JsonSerializer.Deserialize<PluginAnchorGroupDef>(json);

            Assert.Equal(def.Id, round.Id);
            Assert.Equal(def.Label, round.Label);
            Assert.Equal(def.Columns, round.Columns);
            Assert.Equal(def.Rows, round.Rows);
            Assert.Equal(def.Fill, round.Fill);
            Assert.Equal(def.X, round.X);
            Assert.Equal(def.Y, round.Y);
            Assert.Equal(def.Locked, round.Locked);
        }
    }
}
