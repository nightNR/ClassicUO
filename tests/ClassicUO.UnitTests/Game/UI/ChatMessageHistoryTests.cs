// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class ChatMessageHistoryTests
    {
        private static ChatHistoryEntry E(string text, ChatMode mode = ChatMode.Default)
            => new ChatHistoryEntry { Mode = mode, Text = text };

        [Fact]
        public void MovePrevious_WalksBackFromNewest()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            h.Add(E("one"));
            h.Add(E("two"));
            h.Add(E("three"));

            Assert.Equal("three", h.MovePrevious().Text);
            Assert.Equal("two", h.MovePrevious().Text);
            Assert.Equal("one", h.MovePrevious().Text);
            // does not walk past the oldest
            Assert.Equal("one", h.MovePrevious().Text);
        }

        [Fact]
        public void MovePrevious_ReturnsNullWhenEmpty()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            Assert.Null(h.MovePrevious());
        }

        [Fact]
        public void MoveNext_WalksForwardThenSignalsClearAtNewest()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            h.Add(E("one"));
            h.Add(E("two"));
            h.Add(E("three"));

            h.MovePrevious(); // three
            h.MovePrevious(); // two
            h.MovePrevious(); // one

            Assert.True(h.MoveNext(out var e1));
            Assert.Equal("two", e1.Text);
            Assert.True(h.MoveNext(out var e2));
            Assert.Equal("three", e2.Text);
            // at newest -> clear
            Assert.False(h.MoveNext(out var e3));
            Assert.Null(e3);
        }

        [Fact]
        public void Add_TrimsFromFrontToMaxLength()
        {
            var h = new ChatMessageHistory { MaxLength = 2 };
            h.Add(E("one"));
            h.Add(E("two"));
            h.Add(E("three"));

            Assert.Equal(2, h.Count);
            Assert.Equal("two", h.Entries[0].Text);
            Assert.Equal("three", h.Entries[1].Text);
        }

        [Fact]
        public void Load_TrimsAndResetsIndexToNewest()
        {
            var h = new ChatMessageHistory { MaxLength = 2 };
            h.Load(new[] { E("a"), E("b"), E("c") });

            Assert.Equal(2, h.Count);
            Assert.Equal("c", h.MovePrevious().Text); // index started at newest
        }

        [Fact]
        public void MaxLengthZero_DisablesHistory()
        {
            var h = new ChatMessageHistory { MaxLength = 0 };
            h.Add(E("one"));

            Assert.Equal(0, h.Count);
            Assert.Null(h.MovePrevious());
            Assert.False(h.MoveNext(out _));
        }

        [Fact]
        public void MovePrevious_PreservesMode()
        {
            var h = new ChatMessageHistory { MaxLength = 20 };
            h.Add(E("guildmsg", ChatMode.Guild));

            var e = h.MovePrevious();
            Assert.Equal(ChatMode.Guild, e.Mode);
        }
    }
}
