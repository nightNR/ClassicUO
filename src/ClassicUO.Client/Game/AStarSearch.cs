// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;

namespace ClassicUO.Game
{
    /// <summary>
    /// Resumable A* over an implicit 8-connected grid. Holds the frontier
    /// (binary-heap open set + hashed closed set) between <see cref="Step"/>
    /// calls so a long search can be spread across game ticks with a per-call
    /// node budget. Walkability comes from an injected
    /// <see cref="AStarPathSearch.ExpandNeighbors"/> delegate.
    /// </summary>
    internal sealed class AStarSearch
    {
        internal enum Status { Running, Found, Failed }

        private struct SearchNode
        {
            public int X, Y, Z, Direction, G, Parent;
        }

        private readonly List<SearchNode> _nodes = new List<SearchNode>();
        private readonly PriorityQueue<int, int> _open = new PriorityQueue<int, int>();
        private readonly Dictionary<long, int> _bestG = new Dictionary<long, int>();
        private readonly HashSet<long> _closed = new HashSet<long>();
        private readonly List<AStarPathSearch.Neighbor> _neighbors = new List<AStarPathSearch.Neighbor>();

        private int _goalX, _goalY, _distance;
        private AStarPathSearch.ExpandNeighbors _expand;
        private int _goalIndex = -1;
        private bool _startWithinTolerance;

        public int ExpandedNodes { get; private set; }

        private static long Key(int x, int y, int z)
        {
            return ((long)(uint)x << 32) | ((long)(uint)y << 8) | (byte)(sbyte)z;
        }

        private static int Heuristic(int x, int y, int goalX, int goalY)
        {
            int dx = Math.Abs(goalX - x);
            int dy = Math.Abs(goalY - y);

            return dx > dy ? dx : dy;
        }

        public void Begin(int startX, int startY, int startZ, int goalX, int goalY, int distance, AStarPathSearch.ExpandNeighbors expand)
        {
            _nodes.Clear();
            _open.Clear();
            _bestG.Clear();
            _closed.Clear();
            _neighbors.Clear();
            _goalX = goalX;
            _goalY = goalY;
            _distance = distance;
            _expand = expand;
            _goalIndex = -1;
            ExpandedNodes = 0;
            _startWithinTolerance = Heuristic(startX, startY, goalX, goalY) <= distance;

            if (_startWithinTolerance)
            {
                return;
            }

            _nodes.Add(new SearchNode { X = startX, Y = startY, Z = startZ, Direction = 0, G = 0, Parent = -1 });
            _bestG[Key(startX, startY, startZ)] = 0;
            _open.Enqueue(0, Heuristic(startX, startY, goalX, goalY));
        }

        public Status Step(int nodeBudget)
        {
            if (_startWithinTolerance)
            {
                _goalIndex = -1;
                return Status.Found;
            }

            int expandedThisCall = 0;

            while (_open.TryDequeue(out int idx, out _))
            {
                SearchNode cur = _nodes[idx];
                long curKey = Key(cur.X, cur.Y, cur.Z);

                if (!_closed.Add(curKey))
                {
                    continue;
                }

                if (_bestG.TryGetValue(curKey, out int recordedG) && recordedG < cur.G)
                {
                    continue;
                }

                if (Heuristic(cur.X, cur.Y, _goalX, _goalY) <= _distance)
                {
                    _goalIndex = idx;
                    return Status.Found;
                }

                ExpandedNodes++;

                _neighbors.Clear();
                _expand(cur.X, cur.Y, cur.Z, _neighbors);

                for (int i = 0; i < _neighbors.Count; i++)
                {
                    AStarPathSearch.Neighbor nb = _neighbors[i];
                    long nKey = Key(nb.X, nb.Y, nb.Z);

                    if (_closed.Contains(nKey))
                    {
                        continue;
                    }

                    int ng = cur.G + nb.Cost;

                    if (_bestG.TryGetValue(nKey, out int oldG) && oldG <= ng)
                    {
                        continue;
                    }

                    _bestG[nKey] = ng;
                    _nodes.Add(new SearchNode { X = nb.X, Y = nb.Y, Z = nb.Z, Direction = nb.Direction, G = ng, Parent = idx });
                    _open.Enqueue(_nodes.Count - 1, ng + Heuristic(nb.X, nb.Y, _goalX, _goalY));
                }

                if (++expandedThisCall >= nodeBudget)
                {
                    return Status.Running;
                }
            }

            return Status.Failed;
        }

        public void GetPath(List<AStarPathSearch.Step> path)
        {
            path.Clear();

            if (_goalIndex < 0)
            {
                return; // start already within tolerance: no steps
            }

            int count = 0;

            for (int i = _goalIndex; i >= 0 && _nodes[i].Parent != -1; i = _nodes[i].Parent)
            {
                count++;
            }

            for (int i = 0; i < count; i++)
            {
                path.Add(default);
            }

            int slot = count - 1;

            for (int i = _goalIndex; i >= 0 && _nodes[i].Parent != -1; i = _nodes[i].Parent)
            {
                SearchNode n = _nodes[i];
                path[slot--] = new AStarPathSearch.Step(n.X, n.Y, n.Z, n.Direction);
            }
        }
    }
}
