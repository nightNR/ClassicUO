// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Game.Scenes;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassicUO.Game.UI.Gumps
{
    internal partial class CounterBarGump
    {
        private sealed class CounterVitals : Control
        {
            private static readonly Color BackgroundColor = new(32, 32, 32);
            private static readonly Color HealthyColor = new(0, 170, 55);
            private static readonly Color WarningColor = new(220, 185, 0);
            private static readonly Color CriticalColor = new(200, 30, 30);
            private readonly CounterBarGump _gump;
            private CounterBarGump _sourceGump;
            private int _sourceIndex;

            public CounterVitals(CounterBarGump gump)
            {
                _gump = gump;
                AcceptMouseInput = true;
                CanMove = true;
                CanCloseWithRightClick = false;
                WantUpdateSize = false;
            }

            protected override void OnDragBegin(int x, int y)
            {
                if (_gump.ReadOnly)
                {
                    return;
                }

                _sourceGump = Parent?.RootParent as CounterBarGump ?? _gump;
                _sourceIndex = _sourceGump._dataBox.Children.IndexOf(this);

                DraggableGump dragGump = new(_gump.World)
                {
                    X = Mouse.LClickPosition.X - Width / 2,
                    Y = Mouse.LClickPosition.Y - Height / 2
                };

                dragGump.Add(this);
                X = 0;
                Y = 0;
                UIManager.Add(dragGump);
                UIManager.AttemptDragControl(dragGump, true);
                _sourceGump.SetupLayout();
            }

            protected override void OnDragEnd(int x, int y)
            {
                if (Parent is DraggableGump oldParent)
                {
                    FinalizeDragDrop(oldParent, UIManager.GetAllMouseOverControlsOfType<CounterBarGump>());
                }

                base.OnDragEnd(x, y);
            }

            private void FinalizeDragDrop(Control oldParent, IEnumerable<Control> hoveredControls)
            {
                Control hovered = hoveredControls.FirstOrDefault();
                CounterBarGump target = hovered?.RootParent as CounterBarGump;

                if (target == null)
                {
                    target = _sourceGump;
                    target._dataBox.Insert(Math.Clamp(_sourceIndex, 0, target._dataBox.Children.Count), this);
                }
                else if (hovered is CounterItem counterItem)
                {
                    target._dataBox.Insert(target._dataBox.Children.IndexOf(counterItem), this);
                }
                else
                {
                    target._dataBox.Add(this);
                }

                target._vitalsIndex = target._dataBox.Children.IndexOf(this);
                target.SetupLayout();
                oldParent.Dispose();
            }

            internal static float GetPercent(int current, int maximum)
            {
                return maximum <= 0 ? 0f : Math.Clamp(current / (float)maximum, 0f, 1f);
            }

            internal static Color GetColor(float percent)
            {
                return percent > 0.75f
                    ? HealthyColor
                    : percent > 0.20f
                        ? WarningColor
                        : CriticalColor;
            }

            public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
            {
                base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);

                float[] values =
                {
                    GetPercent(_gump.World.Player.Hits, _gump.World.Player.HitsMax),
                    GetPercent(_gump.World.Player.Mana, _gump.World.Player.ManaMax),
                    GetPercent(_gump.World.Player.Stamina, _gump.World.Player.StaminaMax)
                };

                int gap = 1;
                int barHeight = Math.Max(1, (Height - gap * 2) / 3);
                int remainder = Height - barHeight * 3 - gap * 2;
                Vector3 hueVector = ShaderHueTranslator.GetHueVector(0);
                float layerDepth = layerDepthRef;

                renderLists.AddGumpNoAtlas(batcher =>
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        int barY = y + i * (barHeight + gap);
                        int height = barHeight + (i == values.Length - 1 ? remainder : 0);
                        batcher.Draw(
                            SolidColorTextureCache.GetTexture(BackgroundColor),
                            new Rectangle(x, barY, Width, height),
                            hueVector,
                            layerDepth
                        );

                        int fillWidth = (int)MathF.Round(Width * values[i]);
                        if (fillWidth > 0)
                        {
                            batcher.Draw(
                                SolidColorTextureCache.GetTexture(GetColor(values[i])),
                                new Rectangle(x, barY, fillWidth, height),
                                hueVector,
                                layerDepth
                            );
                        }
                    }

                    return true;
                });

                return true;
            }
        }
    }
}
