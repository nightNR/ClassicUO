// SPDX-License-Identifier: BSD-2-Clause

using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Configuration;
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
        private readonly ScrollArea _scrollArea;
        private readonly bool _usePagination;

        private int _currentPage;
        private int _pageCount = 1;

        public GridContainerView(ContainerGump host, bool usePagination, int width, int height)
        {
            _host = host;
            _usePagination = usePagination;

            CanMove = true;
            AcceptMouseInput = true;
            WantUpdateSize = false;

            Width = usePagination ? GridContainerLayout.GridWidth() : width;
            Height = usePagination ? GridContainerLayout.GridHeight() + GridContainerLayout.RESERVED_BOTTOM : height;

            _background = new AlphaBlendControl
            {
                Width = Width,
                Height = Height,
                BaseColor = PanelColor()
            };
            Add(_background);

            if (!usePagination)
            {
                _scrollArea = new ScrollArea(0, 0, Width, Height, true)
                {
                    ScrollbarBehaviour = ScrollbarBehaviour.ShowAlways
                };
                Add(_scrollArea);
            }

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
                IsVisible = false,
                IsEnabled = usePagination
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
                IsVisible = false,
                IsEnabled = usePagination
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

            _pageLabel.IsVisible = usePagination;

            Add(_pageLabel);
        }

        // Backdrop/cell fill color for the grid container. Pale gray when the light-background
        // option is on, otherwise the historical black. Shared with GridContainerItem so the whole
        // panel stays consistent.
        internal static Color PanelColor()
            => ProfileManager.CurrentProfile?.GridContainerLightBackground == true ? Color.LightGray : Color.Black;

        public World World => _host.World;

        public uint ContainerSerial => _host.LocalSerial;

        public int CurrentPage => _currentPage;

        public int ScrollValue => _usePagination ? 0 : _scrollArea.ScrollValue;

        public void SetScrollValue(int value)
        {
            if (!_usePagination)
            {
                _scrollArea.SetScrollValueAfterLayout(value);
            }
        }

        public void RequestSingleClick(uint serial)
        {
            if (World.DelayedObjectClickManager.IsEnabled)
            {
                return;
            }

            Point off = Mouse.LDragOffset;
            World.DelayedObjectClickManager.Set(
                serial,
                Mouse.Position.X - off.X - _host.ScreenCoordinateX,
                Mouse.Position.Y - off.Y - _host.ScreenCoordinateY,
                Time.Ticks + Mouse.MOUSE_DELAY_DOUBLE_CLICK
            );
        }

        public bool TryDropHeldItem(uint targetSerial = 0)
        {
            if (!Client.Game.UO.GameCursor.ItemHold.Enabled
                || Client.Game.UO.GameCursor.ItemHold.IsFixedPosition)
            {
                return false;
            }

            Item container = World.Items.Get(ContainerSerial);
            Entity root = container == null ? null : World.Get(container.RootContainer);

            if (root == null || root.Distance > Constants.DRAG_ITEMS_DISTANCE)
            {
                Client.Game.Audio.PlaySound(0x0051);
                return true;
            }

            uint dropContainer = ContainerSerial;
            int x = 0xFFFF;
            int y = 0xFFFF;
            Item target = World.Items.Get(targetSerial);

            if (target != null)
            {
                if (target.ItemData.IsContainer)
                {
                    dropContainer = target.Serial;
                }
                else if (target.ItemData.IsStackable
                         && target.Graphic == Client.Game.UO.GameCursor.ItemHold.Graphic)
                {
                    dropContainer = target.Serial;
                    x = target.X;
                    y = target.Y;
                }
            }

            GameActions.DropItem(
                Client.Game.UO.GameCursor.ItemHold.Serial,
                x,
                y,
                0,
                dropContainer
            );

            Mouse.CancelDoubleClick = true;
            return true;
        }

        public void Resize(int width, int height)
        {
            if (_usePagination || (Width == width && Height == height))
            {
                return;
            }

            int oldColumns = GridContainerLayout.ColumnsForWidth(Width, true);
            Width = width;
            Height = height;
            _background.Width = width;
            _background.Height = height;
            _scrollArea.Resize(width, height);

            if (oldColumns != GridContainerLayout.ColumnsForWidth(width, true))
            {
                Rebuild();
            }
        }

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
            Control itemParent = _usePagination ? this : _scrollArea;

            foreach (GridContainerItem cell in itemParent.Children.OfType<GridContainerItem>().ToList())
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

                int cx;
                int cy;
                int page;

                if (_usePagination)
                {
                    (cx, cy, page) = GridContainerLayout.CellPosition(index);
                }
                else
                {
                    (cx, cy) = GridContainerLayout.CellPosition(index, GridContainerLayout.ColumnsForWidth(Width, true));
                    page = 0;
                }

                GridContainerItem cell = new GridContainerItem(this, item.Serial, GridContainerLayout.CELL_SIZE)
                {
                    X = cx,
                    Y = cy
                };

                if (_usePagination)
                {
                    Add(cell, page + 1);
                }
                else
                {
                    _scrollArea.Add(cell);
                }

                index++;
            }

            _pageCount = _usePagination ? GridContainerLayout.PageCount(index) : 1;

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
            if (!_usePagination)
            {
                ActivePage = 0;
                return;
            }

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
            if (TryDropHeldItem())
            {
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
