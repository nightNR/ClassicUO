// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace ClassicUO.Game.UI.Gumps
{
    // Pure decision logic for whether a given container serial renders as a grid.
    // Kept free of Profile/UIManager references so it is unit-testable.
    internal static class ContainerViewModeResolver
    {
        // viewMode: 0 = Standard, 1 = Grid, 2 = Toggle.
        public static bool Resolve(
            int viewMode,
            bool toggleDefaultGrid,
            IReadOnlyDictionary<uint, bool> gridStates,
            uint serial
        )
        {
            switch (viewMode)
            {
                case 1:
                    return true;

                case 2:
                    if (gridStates != null && gridStates.TryGetValue(serial, out bool v))
                    {
                        return v;
                    }

                    return toggleDefaultGrid;

                default: // 0 (Standard) and any unknown value
                    return false;
            }
        }

        // Value to store into ContainerGridStates[serial] when the corner toggle is
        // clicked: the inverse of the value currently resolved for this serial.
        public static bool ComputeToggleValue(
            bool toggleDefaultGrid,
            IReadOnlyDictionary<uint, bool> gridStates,
            uint serial
        )
        {
            bool current =
                gridStates != null && gridStates.TryGetValue(serial, out bool v)
                    ? v
                    : toggleDefaultGrid;

            return !current;
        }
    }
}
