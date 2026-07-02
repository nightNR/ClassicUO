// SPDX-License-Identifier: BSD-2-Clause

using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    // Paged grid replacement for a container's item area. Owns all grid layout so
    // ContainerGump stays lean. Populated in container linked-list (draw/stack) order.
    internal sealed class GridContainerView : Control
    {
        private readonly ContainerGump _host;
        private readonly AlphaBlendControl _background;
        private readonly NiceButton _buttonPrev;
        private readonly NiceButton _buttonNext;
        private readonly Label _pageLabel;

        private int _currentPage;
        private int _pageCount = 1;

        public GridContainerView(ContainerGump host)
        {
            _host = host;

            CanMove = true;
            AcceptMouseInput = true;
            WantUpdateSize = false;

            Width = GridContainerLayout.GridWidth();
            Height = GridContainerLayout.GridHeight() + GridContainerLayout.RESERVED_BOTTOM;

            _background = new AlphaBlendControl { Width = Width, Height = Height };
            Add(_background);

            _buttonPrev = new NiceButton(
                4,
                Height - GridContainerLayout.RESERVED_BOTTOM + 5,
                40,
                20,
                ButtonAction.Activate,
                ResGumps.Prev
            )
            {
                ButtonParameter = 0,
                IsSelectable = false,
                IsVisible = false
            };

            _buttonNext = new NiceButton(
                Width - 44,
                Height - GridContainerLayout.RESERVED_BOTTOM + 5,
                40,
                20,
                ButtonAction.Activate,
                ResGumps.Next
            )
            {
                ButtonParameter = 1,
                IsSelectable = false,
                IsVisible = false
            };

            _buttonPrev.MouseUp += (s, e) => { if (e.Button == MouseButtonType.Left) ChangePageBy(-1); };
            _buttonNext.MouseUp += (s, e) => { if (e.Button == MouseButtonType.Left) ChangePageBy(1); };

            Add(_buttonPrev);
            Add(_buttonNext);

            _pageLabel = new Label("1", true, 999, align: TEXT_ALIGN_TYPE.TS_CENTER)
            {
                X = Width / 2 - 5,
                Y = Height - GridContainerLayout.RESERVED_BOTTOM + 5
            };

            Add(_pageLabel);
        }

        public World World => _host.World;

        public uint ContainerSerial => _host.LocalSerial;

        public int CurrentPage => _currentPage;

        public void SetPage(int page)
        {
            _currentPage = page;

            if (_currentPage < 0)
            {
                _currentPage = 0;
            }
            else if (_currentPage >= _pageCount)
            {
                _currentPage = _pageCount - 1;
            }

            if (_currentPage < 0)
            {
                _currentPage = 0;
            }

            ApplyPage();
        }

        public void Rebuild()
        {
            foreach (GridContainerItem cell in Children.OfType<GridContainerItem>().ToList())
            {
                cell.Dispose();
            }

            Entity container = World.Get(ContainerSerial);

            if (container == null)
            {
                return;
            }

            bool isCorpse = container.Graphic == 0x2006;

            int index = 0;

            for (LinkedObject i = container.Items; i != null; i = i.Next)
            {
                Item item = (Item)i;

                if (item.Amount <= 0)
                {
                    continue;
                }

                var layer = (Layer)item.ItemData.Layer;

                if (isCorpse && item.Layer > 0 && !Constants.BAD_CONTAINER_LAYERS[(int)layer])
                {
                    continue;
                }

                if (
                    item.ItemData.IsWearable
                    && (layer == Layer.Face || layer == Layer.Beard || layer == Layer.Hair)
                )
                {
                    continue;
                }

                var (cx, cy, page) = GridContainerLayout.CellPosition(index);

                GridContainerItem cell = new GridContainerItem(this, item.Serial, GridContainerLayout.CELL_SIZE)
                {
                    X = cx,
                    Y = cy
                };

                Add(cell, page + 1);

                index++;
            }

            _pageCount = GridContainerLayout.PageCount(index);

            if (_currentPage >= _pageCount)
            {
                _currentPage = _pageCount - 1;
            }

            if (_currentPage < 0)
            {
                _currentPage = 0;
            }

            ApplyPage();
        }

        private void ChangePageBy(int delta)
        {
            _currentPage += delta;

            if (_currentPage < 0)
            {
                _currentPage = 0;
            }
            else if (_currentPage >= _pageCount)
            {
                _currentPage = _pageCount - 1;
            }

            ApplyPage();
        }

        private void ApplyPage()
        {
            ActivePage = _currentPage + 1;

            _buttonPrev.IsVisible = _currentPage > 0;
            _buttonNext.IsVisible = _currentPage < _pageCount - 1;

            _pageLabel.Text = ActivePage.ToString();
            _pageLabel.X = Width / 2 - _pageLabel.Width / 2;
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button != MouseButtonType.Left)
            {
                base.OnMouseUp(x, y, button);
                return;
            }

            // Empty grid background is a drop target for a held item.
            if (
                Client.Game.UO.GameCursor.ItemHold.Enabled
                && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
            )
            {
                GameActions.DropItem(
                    Client.Game.UO.GameCursor.ItemHold.Serial,
                    0xFFFF,
                    0xFFFF,
                    0,
                    ContainerSerial
                );

                Mouse.CancelDoubleClick = true;
                return;
            }

            base.OnMouseUp(x, y, button);
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            float layerDepth = layerDepthRef;

            Vector3 hueVector = ShaderHueTranslator.GetHueVector(0);

            renderLists.AddGumpNoAtlas(
                batcher =>
                {
                    batcher.DrawRectangle(
                        SolidColorTextureCache.GetTexture(Color.Gray),
                        x,
                        y,
                        Width,
                        Height,
                        hueVector,
                        layerDepth
                    );
                    return true;
                }
            );

            return true;
        }
    }
}
