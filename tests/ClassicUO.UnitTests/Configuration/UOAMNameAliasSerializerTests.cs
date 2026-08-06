// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Configuration
{
    public class UOAMNameAliasSerializerTests
    {
        [Fact]
        public void Parse_EmptyString_ReturnsEmptyDictionary()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("");

            Assert.Empty(result);
        }

        [Fact]
        public void Parse_SinglePair_ReturnsOneEntry()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("UOAMBob=Bobbicus");

            Assert.Single(result);
            Assert.Equal("Bobbicus", result["UOAMBob"]);
        }

        [Fact]
        public void Parse_MultiplePairs_ReturnsAllEntries()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("A=B;C=D");

            Assert.Equal(2, result.Count);
            Assert.Equal("B", result["A"]);
            Assert.Equal("D", result["C"]);
        }

        [Fact]
        public void Parse_TrimsWhitespaceAroundKeysAndValues()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse(" A = B ; C = D ");

            Assert.Equal("B", result["A"]);
            Assert.Equal("D", result["C"]);
        }

        [Fact]
        public void Parse_SkipsMalformedEntries()
        {
            Dictionary<string, string> result = UOAMNameAliasSerializer.Parse("A=B;NoEqualsSign;C=D");

            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("A"));
            Assert.True(result.ContainsKey("C"));
        }

        [Fact]
        public void Serialize_RoundTripsThroughParse()
        {
            var aliases = new Dictionary<string, string> { ["A"] = "B", ["C"] = "D" };

            string serialized = UOAMNameAliasSerializer.Serialize(aliases);
            Dictionary<string, string> parsed = UOAMNameAliasSerializer.Parse(serialized);

            Assert.Equal(2, parsed.Count);
            Assert.Equal("B", parsed["A"]);
            Assert.Equal("D", parsed["C"]);
        }
    }
}
