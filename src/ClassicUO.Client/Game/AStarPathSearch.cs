// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game
{
    /// <summary>
    /// Pure A* graph search over an implicit 8-connected grid. The frontier uses
    /// a binary heap (<see cref="PriorityQueue{TElement,TPriority}"/>) for
    /// cheapest-node selection and hash sets for closed/open membership, giving
    /// O(n log n) behaviour instead of the legacy O(n^2) fixed-array scans. It is
    /// map-agnostic: walkability and step cost come from an injected
    /// <see cref="ExpandNeighbors"/> delegate, which makes it unit-testable
    /// without UO data and lets <see cref="Pathfinder"/> supply CanWalk-based
    /// expansion.
    /// </summary>
    internal static class AStarPathSearch
    {
        internal readonly struct Neighbor
        {
            public Neighbor(int x, int y, int z, int direction, int cost)
            {
                X = x;
                Y = y;
                Z = z;
                Direction = direction;
                Cost = cost;
            }

            public int X { get; }
            public int Y { get; }
            public int Z { get; }
            public int Direction { get; }
            public int Cost { get; }
        }

        internal readonly struct Step
        {
            public Step(int x, int y, int z, int direction)
            {
                X = x;
                Y = y;
                Z = z;
                Direction = direction;
            }

            public int X { get; }
            public int Y { get; }
            public int Z { get; }
            public int Direction { get; }
        }

        /// <summary>Fill <paramref name="into"/> (already cleared by the caller)
        /// with the walkable neighbours of (<paramref name="x"/>,
        /// <paramref name="y"/>, <paramref name="z"/>).</summary>
        internal delegate void ExpandNeighbors(int x, int y, int z, List<Neighbor> into);

        /// <summary>
        /// Search for a path from the start tile to within
        /// <paramref name="distance"/> (Chebyshev) tiles of the goal. On success
        /// <paramref name="path"/> is filled with the ordered steps from the
        /// first move up to and including the reached node (empty if the start is
        /// already within tolerance). <paramref name="maxNodes"/> caps the number
        /// of expanded nodes; the search returns false if it is exhausted before
        /// the goal is reached.
        /// </summary>
        internal static bool TryFindPath(
            int startX, int startY, int startZ,
            int goalX, int goalY, int distance,
            int maxNodes,
            ExpandNeighbors expand,
            List<Step> path)
        {
            var search = new AStarSearch();
            search.Begin(startX, startY, startZ, goalX, goalY, distance, expand);

            AStarSearch.Status status = search.Step(maxNodes);

            if (status == AStarSearch.Status.Found)
            {
                search.GetPath(path);
                return true;
            }

            path.Clear();
            return false; // Running (budget exhausted) or Failed — matches legacy maxNodes cap
        }
    }
}
