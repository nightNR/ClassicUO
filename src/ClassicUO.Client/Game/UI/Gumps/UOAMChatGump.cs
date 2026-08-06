// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Configuration;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using ClassicUO.Utility.Collections;

namespace ClassicUO.Game.UI.Gumps
{
    internal class UOAMChatGump : Gump
    {
        private const int DIFF_Y = 22;
        private readonly ExpandableScroll _background;
        private readonly UOAMChatEntryList _entries;
        private readonly ScrollFlag _scrollBar;
        private readonly StbTextBox _inputBox;

        public UOAMChatGump(World world) : base(world, 0, 0)
        {
            Width = 320;
            Height = 260;
            CanMove = true;
            CanCloseWithRightClick = true;

            Add
            (
                _background = new ExpandableScroll(0, DIFF_Y, Height - DIFF_Y, 0x1F40)
                {
                    TitleGumpID = 0x82A
                }
            );

            _scrollBar = new ScrollFlag(-25, DIFF_Y + 10, Height - DIFF_Y - 30, true);

            Add
            (
                _entries = new UOAMChatEntryList(15, DIFF_Y + 10, Width - 45, Height - DIFF_Y - 40, _scrollBar)
            );

            Add(_scrollBar);

            _inputBox = new StbTextBox(ProfileManager.CurrentProfile.ChatFont, 200, Width - 30, true, FontStyle.BlackBorder, 0)
            {
                X = 15,
                Y = Height - 26,
                Width = Width - 30,
                Height = 20
            };
            Add(_inputBox);

            World.UOAM.ChatTextReceived += World_UOAM_ChatTextReceived;
        }

        public override void OnKeyboardReturn(int textID, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            World.UOAM.SendChatText(text);
            _entries.AddEntry($"{ProfileManager.CurrentProfile.UOAMPlayerName}: {text}");
            _inputBox.SetText(string.Empty);
        }

        private void World_UOAM_ChatTextReceived(string text)
        {
            _entries.AddEntry(text);
        }

        public override void Dispose()
        {
            World.UOAM.ChatTextReceived -= World_UOAM_ChatTextReceived;
            base.Dispose();
        }
    }

    // A minimal, purpose-built scrolling text list for UOAM chat. Not shared with
    // JournalGump's private RenderedTextList: that class carries journal-specific
    // TextType filtering (System/Object/Client/GuildAlly checkboxes) this gump has no
    // use for. Structure (Deque<RenderedText> + ScrollBarBase-driven partial-entry
    // clipping) mirrors it because the visual behavior should match.
    internal class UOAMChatEntryList : Control
    {
        private readonly Deque<RenderedText> _entries;
        private readonly ScrollBarBase _scrollBar;

        public UOAMChatEntryList(int x, int y, int width, int height, ScrollBarBase scrollBarControl)
        {
            _scrollBar = scrollBarControl;
            _scrollBar.IsVisible = false;
            AcceptMouseInput = true;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            _entries = new Deque<RenderedText>();
            WantUpdateSize = false;
        }

        public void AddEntry(string text)
        {
            bool maxScroll = _scrollBar.Value == _scrollBar.MaxValue;

            while (_entries.Count > 199)
            {
                _entries.RemoveFromFront().Destroy();
            }

            RenderedText rtext = RenderedText.Create(text, 0, 1, true, FontStyle.Indention | FontStyle.BlackBorder, maxWidth: Width);
            _entries.AddToBack(rtext);

            _scrollBar.MaxValue += rtext.Height;

            if (maxScroll)
            {
                _scrollBar.Value = _scrollBar.MaxValue;
            }
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            float layerDepth = layerDepthRef;

            int mx = x;
            int my = y;
            int height = 0;
            int maxheight = _scrollBar.Value + _scrollBar.Height;

            renderLists.AddGumpNoAtlas(batcher =>
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    RenderedText t = _entries[i];

                    if (height + t.Height <= _scrollBar.Value)
                    {
                        height += t.Height;
                        continue;
                    }

                    if (height + t.Height <= maxheight)
                    {
                        int yy = height - _scrollBar.Value;

                        if (yy < 0)
                        {
                            t.Draw(batcher, t.Width, t.Height, mx, y, t.Width, t.Height + yy, 0, -yy, layerDepth);
                            my += t.Height + yy;
                        }
                        else
                        {
                            t.Draw(batcher, mx, my, layerDepth);
                            my += t.Height;
                        }

                        height += t.Height;
                    }
                    else
                    {
                        break;
                    }
                }

                return true;
            });

            return true;
        }

        public override void Update()
        {
            base.Update();

            if (!IsVisible)
            {
                return;
            }

            _scrollBar.X = X + Width - (_scrollBar.Width >> 1) + 5;
            _scrollBar.Height = Height;

            int totalHeight = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                totalHeight += _entries[i].Height;
            }

            bool wasMax = _scrollBar.Value == _scrollBar.MaxValue;
            int max = totalHeight - _scrollBar.Height;
            _scrollBar.MaxValue = max > 0 ? max : 0;

            if (wasMax)
            {
                _scrollBar.Value = _scrollBar.MaxValue;
            }

            _scrollBar.IsVisible = _scrollBar.MaxValue > _scrollBar.MinValue;
        }
    }
}
