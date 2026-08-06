// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Network.UOAM;
using Xunit;

namespace ClassicUO.UnitTests.Network.UOAM
{
    public class UOAMPlayerInfoTests
    {
        [Fact]
        public void Encode_WritesNameXYZMap_RoundTripsThroughDecode()
        {
            Span<byte> buffer = new byte[92];

            UOAMPlayerInfoCodec.Encode(buffer, "Bazinka", 1234, 5678, 10, 1);

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out UOAMPlayerInfo info);

            Assert.True(ok);
            Assert.Equal("Bazinka", info.Name);
            Assert.Equal(1234, info.X);
            Assert.Equal(5678, info.Y);
            Assert.Equal(10, info.Z);
            Assert.Equal(1, info.Map);
        }

        [Fact]
        public void Encode_TruncatesNameTo63Chars_LeavingRoomForNul()
        {
            Span<byte> buffer = new byte[92];
            string longName = new string('A', 100);

            UOAMPlayerInfoCodec.Encode(buffer, longName, 0, 0, 0, 0);

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out UOAMPlayerInfo info);

            Assert.True(ok);
            Assert.Equal(63, info.Name.Length);
        }

        [Fact]
        public void Encode_ZeroesReservedBytes()
        {
            Span<byte> buffer = new byte[92];
            buffer.Fill(0xFF);

            UOAMPlayerInfoCodec.Encode(buffer, "X", 0, 0, 0, 0);

            // reserved[12] lives at offset 64..75 (after name[64])
            for (int i = 64; i < 76; i++)
            {
                Assert.Equal(0, buffer[i]);
            }
        }

        [Fact]
        public void TryDecode_ReturnsFalse_WhenBufferTooShort()
        {
            Span<byte> buffer = new byte[91];

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryDecode_ReturnsFalse_WhenNameHasNoNulTerminator()
        {
            Span<byte> buffer = new byte[92];
            buffer.Slice(0, 64).Fill((byte)'A'); // no NUL anywhere in the 64-byte name field

            bool ok = UOAMPlayerInfoCodec.TryDecode(buffer, out _);

            Assert.False(ok);
        }
    }
}
