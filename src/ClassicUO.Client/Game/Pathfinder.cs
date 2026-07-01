// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Assets;
using Microsoft.Xna.Framework;
using MathHelper = ClassicUO.Utility.MathHelper;

namespace ClassicUO.Game
{
    /// <summary>State of an auto-walk. Mirrors ClassicUO.PluginApi.WalkState
    /// (same ordinals); the int value crosses the plugin FFI boundary.</summary>
    internal enum WalkState
    {
        Walking,
        Arrived,
        Blocked,
        Stopped,
    }

    internal sealed class Pathfinder
    {
        // Safety cap on expanded nodes. With the O(n log n) heap-based search
        // (see AStarPathSearch) this can be far larger than the legacy 10000
        // O(n^2) limit without freezing, so full-map routes succeed. It only
        // guards against runaway searches toward genuinely unreachable goals.
        private const int PATHFINDER_MAX_NODES = 300000;

        // Per-frame node budgets for the resumable search. The first call spends
        // a large budget so in-view walks finish synchronously (no behaviour
        // change); longer searches spill into per-tick continuations driven by
        // ProcessAutoWalk.
        internal const int FIRST_CALL_NODE_BUDGET = 8000;
        internal const int PER_TICK_NODE_BUDGET = 4000;

        internal static int FrameBudget(bool firstCall) => firstCall ? FIRST_CALL_NODE_BUDGET : PER_TICK_NODE_BUDGET;

        private readonly List<AStarPathSearch.Step> _path = new List<AStarPathSearch.Step>();
        private int _pointIndex, _pathSize;
        private bool _run;
        private readonly AStarSearch _search = new AStarSearch();
        private bool _searching;
        private static readonly int[] _offsetX =
        {
            0, 1, 1, 1, 0, -1, -1, -1, 0, 1
        };
        private static readonly int[] _offsetY =
        {
            -1, -1, 0, 1, 1, 1, 0, -1, -1, -1
        };
        private static readonly sbyte[] _dirOffset =
        {
            1, -1
        };

        private readonly World _world;

        public Pathfinder(World world)
        {
            _world = world;
        }

        public bool AutoWalking { get; set; }

        public bool PathindingCanBeCancelled { get; set; }

        /// <summary>True while a computed auto-walk path is being followed (not
        /// during the search phase). Consumers like the world map read the path
        /// for display.</summary>
        internal bool HasActivePath => AutoWalking && !_searching && _pathSize > 0;

        /// <summary>The computed path, start→goal, in world-tile coordinates.
        /// Only the first <see cref="CurrentPathSize"/> entries are valid; walk
        /// progress is at <see cref="CurrentPathIndex"/>.</summary>
        internal IReadOnlyList<AStarPathSearch.Step> CurrentPath => _path;

        internal int CurrentPathIndex => _pointIndex;

        internal int CurrentPathSize => _pathSize;

        /// <summary>Raised on auto-walk state transitions (game thread). The
        /// plugin bridge subscribes; see GameController / Network.Plugin.</summary>
        internal static event Action<WalkState> WalkProgress;

        public bool BlockMoving { get; set; }

        public bool FastRotation { get; set; }


        private bool CreateItemList(List<PathObject> list, int x, int y, int stepState)
        {
            // load: true. The client only keeps map chunks loaded in a window
            // around the player, so the live A* used to see any tile outside that
            // window as null == impassable — silently capping WalkTo range to
            // roughly the view distance regardless of the region map. Loading the
            // chunk on demand lets the search traverse far same-region routes.
            // Chunks touched here refresh their LastAccessTime and are reclaimed
            // by ClearUnusedBlocks shortly after the search moves on. This runs on
            // the game thread (WalkTo / ProcessAutoWalk), same as normal chunk
            // streaming, so it is safe. Walkability semantics are unchanged for
            // tiles that were already loaded.
            GameObject tile = _world.Map.GetTile(x, y, true);

            if (tile == null)
            {
                return false;
            }

            bool ignoreGameCharacters = ProfileManager.CurrentProfile.IgnoreStaminaCheck || stepState == (int) PATH_STEP_STATE.PSS_DEAD_OR_GM || _world.Player.IgnoreCharacters || !(_world.Player.Stamina < _world.Player.StaminaMax && _world.Map.Index == 0);

            bool isGM = _world.Player.Graphic == 0x03DB;

            GameObject obj = tile;

            while (obj.TPrevious != null)
            {
                obj = obj.TPrevious;
            }

            for (; obj != null; obj = obj.TNext)
            {
                if (_world.CustomHouseManager != null && obj.Z < _world.Player.Z)
                {
                    continue;
                }

                ushort graphicHelper = obj.Graphic;

                switch (obj)
                {
                    case Land tile1:

                        if (graphicHelper < 0x01AE && graphicHelper != 2 || graphicHelper > 0x01B5 && graphicHelper != 0x01DB)
                        {
                            uint flags = (uint) PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE;

                            if (stepState == (int) PATH_STEP_STATE.PSS_ON_SEA_HORSE)
                            {
                                if (tile1.TileData.IsWet)
                                {
                                    flags = (uint) (PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE | PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE);
                                }
                            }
                            else
                            {
                                if (!tile1.TileData.IsImpassable)
                                {
                                    flags = (uint) (PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE | PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE);
                                }

                                if (stepState == (int) PATH_STEP_STATE.PSS_FLYING && tile1.TileData.IsNoDiagonal)
                                {
                                    flags |= (uint) PATH_OBJECT_FLAGS.POF_NO_DIAGONAL;
                                }
                            }

                            int landMinZ = tile1.MinZ;
                            int landAverageZ = tile1.AverageZ;
                            int landHeight = landAverageZ - landMinZ;

                            list.Add
                            (
                                new PathObject
                                (
                                    flags,
                                    landMinZ,
                                    landAverageZ,
                                    landHeight,
                                    obj
                                )
                            );
                        }

                        break;

                    case GameEffect _: break;

                    default:
                        bool canBeAdd = true;
                        bool dropFlags = false;

                        switch (obj)
                        {
                            case Mobile mobile:
                            {
                                if (!ignoreGameCharacters && !mobile.IsDead && !mobile.IgnoreCharacters)
                                {
                                    list.Add
                                    (
                                        new PathObject
                                        (
                                            (uint) PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE,
                                            mobile.Z,
                                            mobile.Z + Constants.DEFAULT_CHARACTER_HEIGHT,
                                            Constants.DEFAULT_CHARACTER_HEIGHT,
                                            mobile
                                        )
                                    );
                                }

                                canBeAdd = false;

                                break;
                            }

                            case Item item when item.IsMulti || item.ItemData.IsInternal:
                            {
                                //canBeAdd = false;

                                break;
                            }

                            case Item item2:
                                if (stepState == (int) PATH_STEP_STATE.PSS_DEAD_OR_GM && (item2.ItemData.IsDoor || item2.ItemData.Weight <= 0x5A || isGM && !item2.IsLocked))
                                {
                                    dropFlags = true;
                                }
                                else if (ProfileManager.CurrentProfile.SmoothDoors && item2.ItemData.IsDoor)
                                {
                                    dropFlags = true;
                                }
                                else
                                {
                                    dropFlags = graphicHelper >= 0x3946 && graphicHelper <= 0x3964 || graphicHelper == 0x0082;
                                }

                                break;

                            case Multi m:

                                if ((_world.CustomHouseManager != null && m.IsCustom && (m.State & CUSTOM_HOUSE_MULTI_OBJECT_FLAGS.CHMOF_GENERIC_INTERNAL) == 0) || m.IsHousePreview)
                                {
                                    canBeAdd = false;
                                }

                                if ((m.State & CUSTOM_HOUSE_MULTI_OBJECT_FLAGS.CHMOF_IGNORE_IN_RENDER) != 0)
                                {
                                    dropFlags = true;
                                }

                                break;
                        }

                        if (canBeAdd)
                        {
                            uint flags = 0;

                            if (!(obj is Mobile))
                            {
                                var graphic = obj is Item it && it.IsMulti ? it.MultiGraphic : obj.Graphic;
                                ref StaticTiles itemdata = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];

                                if (stepState == (int) PATH_STEP_STATE.PSS_ON_SEA_HORSE)
                                {
                                    if (itemdata.IsWet)
                                    {
                                        flags = (uint) (PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE);
                                    }
                                }
                                else
                                {
                                    if (itemdata.IsImpassable || itemdata.IsSurface)
                                    {
                                        flags = (uint) PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE;
                                    }

                                    if (!itemdata.IsImpassable)
                                    {
                                        if (itemdata.IsSurface)
                                        {
                                            flags |= (uint) PATH_OBJECT_FLAGS.POF_SURFACE;
                                        }

                                        if (itemdata.IsBridge)
                                        {
                                            flags |= (uint) PATH_OBJECT_FLAGS.POF_BRIDGE;
                                        }
                                    }

                                    if (stepState == (int) PATH_STEP_STATE.PSS_DEAD_OR_GM)
                                    {
                                        if (graphicHelper <= 0x0846)
                                        {
                                            if (!(graphicHelper != 0x0846 && graphicHelper != 0x0692 && (graphicHelper <= 0x06F4 || graphicHelper > 0x06F6)))
                                            {
                                                dropFlags = true;
                                            }
                                        }
                                        else if (graphicHelper == 0x0873)
                                        {
                                            dropFlags = true;
                                        }
                                    }

                                    if (dropFlags)
                                    {
                                        flags &= 0xFFFFFFFE;
                                    }

                                    if (stepState == (int) PATH_STEP_STATE.PSS_FLYING && itemdata.IsNoDiagonal)
                                    {
                                        flags |= (uint) PATH_OBJECT_FLAGS.POF_NO_DIAGONAL;
                                    }
                                }

                                if (flags != 0)
                                {
                                    int objZ = obj.Z;
                                    int staticHeight = itemdata.Height;
                                    int staticAverageZ = staticHeight;

                                    if (itemdata.IsBridge)
                                    {
                                        staticAverageZ /= 2;
                                        // revert fix from fwiffo because it causes unwalkable stairs [down --> up]
                                        //staticAverageZ += staticHeight % 2;
                                    }

                                    list.Add
                                    (
                                        new PathObject
                                        (
                                            flags,
                                            objZ,
                                            staticAverageZ + objZ,
                                            staticHeight,
                                            obj
                                        )
                                    );
                                }
                            }
                        }

                        break;
                }
            }

            return list.Count != 0;
        }

        private int CalculateMinMaxZ
        (
            ref int minZ,
            ref int maxZ,
            int newX,
            int newY,
            int currentZ,
            int newDirection,
            int stepState
        )
        {
            minZ = -128;
            maxZ = currentZ;
            newDirection &= 7;
            int direction = newDirection ^ 4;
            newX += _offsetX[direction];
            newY += _offsetY[direction];
            List<PathObject> list = new List<PathObject>();

            if (!CreateItemList(list, newX, newY, stepState) || list.Count == 0)
            {
                return 0;
            }

            foreach (PathObject obj in list)
            {
                GameObject o = obj.Object;
                int averageZ = obj.AverageZ;

                if (averageZ <= currentZ && o is Land tile && tile.IsStretched)
                {
                    int avgZ = tile.CalculateCurrentAverageZ(newDirection);

                    if (minZ < avgZ)
                    {
                        minZ = avgZ;
                    }

                    if (maxZ < avgZ)
                    {
                        maxZ = avgZ;
                    }
                }
                else
                {
                    if ((obj.Flags & (uint) PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE) != 0 && averageZ <= currentZ && minZ < averageZ)
                    {
                        minZ = averageZ;
                    }

                    if ((obj.Flags & (uint) PATH_OBJECT_FLAGS.POF_BRIDGE) != 0 && currentZ == averageZ)
                    {
                        int z = obj.Z;
                        int height = z + obj.Height;

                        if (maxZ < height)
                        {
                            maxZ = height;
                        }

                        if (minZ > z)
                        {
                            minZ = z;
                        }
                    }
                }
            }

            maxZ += 2;

            return maxZ;
        }

        public bool CalculateNewZ(int x, int y, ref sbyte z, int direction)
        {
            int stepState = (int) PATH_STEP_STATE.PSS_NORMAL;

            if (_world.Player.IsDead || _world.Player.Graphic == 0x03DB)
            {
                stepState = (int) PATH_STEP_STATE.PSS_DEAD_OR_GM;
            }
            else
            {
                if (_world.Player.IsGargoyle && _world.Player.IsFlying)
                {
                    stepState = (int) PATH_STEP_STATE.PSS_FLYING;
                }
                else
                {
                    Item mount = _world.Player.FindItemByLayer(Layer.Mount);

                    if (mount != null && mount.Graphic == 0x3EB3) // sea horse
                    {
                        stepState = (int) PATH_STEP_STATE.PSS_ON_SEA_HORSE;
                    }
                }
            }

            int minZ = -128;
            int maxZ = z;

            CalculateMinMaxZ
            (
                ref minZ,
                ref maxZ,
                x,
                y,
                z,
                direction,
                stepState
            );

            List<PathObject> list = new List<PathObject>();

            if (_world.CustomHouseManager != null)
            {
                Rectangle rect = new Rectangle(_world.CustomHouseManager.StartPos.X, _world.CustomHouseManager.StartPos.Y, _world.CustomHouseManager.EndPos.X, _world.CustomHouseManager.EndPos.Y);

                if (!rect.Contains(x, y))
                {
                    return false;
                }
            }

            if (!CreateItemList(list, x, y, stepState) || list.Count == 0)
            {
                return false;
            }

            list.Sort();

            list.Add
            (
                new PathObject
                (
                    (uint) PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE,
                    128,
                    128,
                    128,
                    null
                )
            );

            int resultZ = -128;

            if (z < minZ)
            {
                z = (sbyte) minZ;
            }

            int currentTempObjZ = 1000000;
            int currentZ = -128;

            for (int i = 0; i < list.Count; i++)
            {
                PathObject obj = list[i];

                if ((obj.Flags & (uint) PATH_OBJECT_FLAGS.POF_NO_DIAGONAL) != 0 && stepState == (int) PATH_STEP_STATE.PSS_FLYING)
                {
                    int objAverageZ = obj.AverageZ;
                    int delta = Math.Abs(objAverageZ - z);

                    if (delta <= 25)
                    {
                        resultZ = objAverageZ != -128 ? objAverageZ : currentZ;

                        break;
                    }
                }

                if ((obj.Flags & (uint) PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE) != 0)
                {
                    int objZ = obj.Z;

                    if (objZ - minZ >= Constants.DEFAULT_BLOCK_HEIGHT)
                    {
                        for (int j = i - 1; j >= 0; j--)
                        {
                            PathObject tempObj = list[j];

                            if ((tempObj.Flags & (uint) (PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE)) != 0)
                            {
                                int tempAverageZ = tempObj.AverageZ;

                                if (tempAverageZ >= currentZ && objZ - tempAverageZ >= Constants.DEFAULT_BLOCK_HEIGHT && (tempAverageZ <= maxZ && (tempObj.Flags & (uint) PATH_OBJECT_FLAGS.POF_SURFACE) != 0 || (tempObj.Flags & (uint) PATH_OBJECT_FLAGS.POF_BRIDGE) != 0 && tempObj.Z <= maxZ))
                                {
                                    int delta = Math.Abs(z - tempAverageZ);

                                    if (delta < currentTempObjZ)
                                    {
                                        currentTempObjZ = delta;
                                        resultZ = tempAverageZ;
                                    }
                                }
                            }
                        }
                    }

                    int averageZ = obj.AverageZ;

                    if (minZ < averageZ)
                    {
                        minZ = averageZ;
                    }

                    if (currentZ < averageZ)
                    {
                        currentZ = averageZ;
                    }
                }
            }

            z = (sbyte) resultZ;

            return resultZ != -128;
        }

        public static void GetNewXY(byte direction, ref int x, ref int y)
        {
            switch (direction & 7)
            {
                case 0:

                {
                    y--;

                    break;
                }

                case 1:

                {
                    x++;
                    y--;

                    break;
                }

                case 2:

                {
                    x++;

                    break;
                }

                case 3:

                {
                    x++;
                    y++;

                    break;
                }

                case 4:

                {
                    y++;

                    break;
                }

                case 5:

                {
                    x--;
                    y++;

                    break;
                }

                case 6:

                {
                    x--;

                    break;
                }

                case 7:

                {
                    x--;
                    y--;

                    break;
                }
            }
        }

        public bool CanWalk(ref Direction direction, ref int x, ref int y, ref sbyte z)
        {
            int newX = x;
            int newY = y;
            sbyte newZ = z;
            byte newDirection = (byte) direction;
            GetNewXY((byte) direction, ref newX, ref newY);
            bool passed = CalculateNewZ(newX, newY, ref newZ, (byte) direction);

            if ((sbyte) direction % 2 != 0)
            {
                if (passed)
                {
                    for (int i = 0; i < 2 && passed; i++)
                    {
                        int testX = x;
                        int testY = y;
                        sbyte testZ = z;
                        byte testDir = (byte) (((byte) direction + _dirOffset[i]) % 8);
                        GetNewXY(testDir, ref testX, ref testY);
                        passed = CalculateNewZ(testX, testY, ref testZ, testDir);
                    }
                }

                if (!passed)
                {
                    for (int i = 0; i < 2 && !passed; i++)
                    {
                        newX = x;
                        newY = y;
                        newZ = z;
                        newDirection = (byte) (((byte) direction + _dirOffset[i]) % 8);
                        GetNewXY(newDirection, ref newX, ref newY);
                        passed = CalculateNewZ(newX, newY, ref newZ, newDirection);
                    }
                }
            }

            if (passed)
            {
                x = newX;
                y = newY;
                z = newZ;
                direction = (Direction) newDirection;
            }

            return passed;
        }

        /// <summary>
        /// Neighbour generator for <see cref="AStarPathSearch"/>: emits the
        /// walkable tiles reachable in one step from (<paramref name="nx"/>,
        /// <paramref name="ny"/>, <paramref name="nz"/>). Mirrors the legacy
        /// OpenNodes expansion — it drives the same CanWalk logic (including the
        /// diagonal-corner check), only the frontier bookkeeping changed.
        /// </summary>
        private void ExpandWalkable(int nx, int ny, int nz, List<AStarPathSearch.Neighbor> into)
        {
            for (int i = 0; i < 8; i++)
            {
                Direction direction = (Direction) i;
                int x = nx;
                int y = ny;
                sbyte z = (sbyte) nz;
                Direction oldDirection = direction;

                if (CanWalk(ref direction, ref x, ref y, ref z))
                {
                    if (direction != oldDirection)
                    {
                        continue;
                    }

                    int diagonal = i % 2;

                    if (diagonal != 0)
                    {
                        Direction wantDirection = (Direction) i;
                        int wantX = nx;
                        int wantY = ny;
                        GetNewXY((byte) wantDirection, ref wantX, ref wantY);

                        if (x != wantX || y != wantY)
                        {
                            diagonal = -1;
                        }
                    }

                    if (diagonal >= 0)
                    {
                        into.Add(new AStarPathSearch.Neighbor(x, y, z, (int) direction, diagonal == 0 ? 1 : 2));
                    }
                }
            }
        }

        public bool WalkTo(int x, int y, int z, int distance, bool run)
        {
            if (_world.Player == null /*|| World.Player.Stamina == 0*/ || _world.Player.IsParalyzed)
            {
                return false;
            }

            if (RegionGate.IsUnreachable(_world.RegionMap, _world.Player.X, _world.Player.Y, x, y))
            {
                return false;
            }

            if (distance == 0 && IsBlocked(x, y, z))
            {
                // Don't do time-consuming computations and freeze the client if the final field is not reachable
                // instead we should just move to a field beside it
                // This is only one edge case but it should be one of the most common ones that we can catch early and cheaply

                // For example pathfinding to a tree, to a treasure chest, to a rock, etc.
                distance = 1;
            }

            int playerX = _world.Player.X;
            int playerY = _world.Player.Y;
            sbyte playerZ = (sbyte) _world.Player.Z;

            _pathSize = 0;
            _pointIndex = 0;
            PathindingCanBeCancelled = true;
            ResetAutoWalk();

            // Legacy behaviour: long hauls always run.
            if (MathHelper.GetDistance(new Point(playerX, playerY), new Point(x, y)) > 14)
            {
                run = true;
            }

            _run = run;
            AutoWalking = true;

            _search.Begin(playerX, playerY, playerZ, x, y, distance, ExpandWalkable);
            _searching = true;

            AStarSearch.Status status = _search.Step(FrameBudget(firstCall: true));

            if (status == AStarSearch.Status.Found)
            {
                AdoptFoundPath();
                return _pathSize != 0;
            }

            if (status == AStarSearch.Status.Failed)
            {
                _searching = false;
                AutoWalking = false;
                return false;
            }

            // Still searching: keep going on later ticks. Report success (a walk
            // has been started) so click-to-path callers do not run their fallback.
            return true;
        }

        private bool IsBlocked(int x, int y, int z)
        {
            sbyte tempZ = (sbyte)z;

            return !CalculateNewZ(x, y, ref tempZ, (byte)Direction.North);
        }

        private void AdoptFoundPath()
        {
            _searching = false;
            _search.GetPath(_path);
            _pathSize = _path.Count;
            _pointIndex = 0;

            if (_pathSize == 0)
            {
                // Already within tolerance: nothing to walk.
                AutoWalking = false;
                return;
            }

            RaiseWalkProgress(WalkState.Walking);
            ProcessAutoWalk();
        }

        public void ProcessAutoWalk()
        {
            if (_searching)
            {
                if (!AutoWalking || !_world.InGame)
                {
                    return;
                }

                AStarSearch.Status status = _search.Step(FrameBudget(firstCall: false));

                if (status == AStarSearch.Status.Found)
                {
                    AdoptFoundPath();
                }
                else if (status == AStarSearch.Status.Failed)
                {
                    EndAutoWalk(WalkState.Blocked);
                }

                return; // do not walk on the tick the search advances/finishes
            }

            if (AutoWalking && _world.InGame && _world.Player.Walker.StepsCount < Constants.MAX_STEP_COUNT && _world.Player.Walker.LastStepRequestTime <= Time.Ticks)
            {
                if (_pointIndex >= 0 && _pointIndex < _pathSize)
                {
                    AStarPathSearch.Step p = _path[_pointIndex];

                    _world.Player.GetEndPosition(out int x, out int y, out sbyte z, out Direction dir);

                    if (dir == (Direction) p.Direction)
                    {
                        _pointIndex++;
                    }

                    if (!_world.Player.Walk((Direction) p.Direction, _run))
                    {
                        EndAutoWalk(WalkState.Blocked);
                    }
                }
                else
                {
                    EndAutoWalk(WalkState.Arrived);
                }
            }
        }

        public void StopAutoWalk()
        {
            EndAutoWalk(WalkState.Stopped);
        }

        internal void ResetAutoWalk()
        {
            AutoWalking = false;
            _run = false;
            _pathSize = 0;
            _searching = false;
        }

        internal void EndAutoWalk(WalkState reason)
        {
            ResetAutoWalk();
            RaiseWalkProgress(reason);
        }

        private static void RaiseWalkProgress(WalkState reason)
        {
            WalkProgress?.Invoke(reason);
        }

        private enum PATH_STEP_STATE
        {
            PSS_NORMAL = 0,
            PSS_DEAD_OR_GM,
            PSS_ON_SEA_HORSE,
            PSS_FLYING
        }

        [Flags]
        private enum PATH_OBJECT_FLAGS : uint
        {
            POF_IMPASSABLE_OR_SURFACE = 0x00000001,
            POF_SURFACE = 0x00000002,
            POF_BRIDGE = 0x00000004,
            POF_NO_DIAGONAL = 0x00000008
        }

        private class PathObject : IComparable<PathObject>
        {
            public PathObject(uint flags, int z, int avgZ, int h, GameObject obj)
            {
                Flags = flags;
                Z = z;
                AverageZ = avgZ;
                Height = h;
                Object = obj;
            }

            public uint Flags { get; }

            public int Z { get; }

            public int AverageZ { get; }

            public int Height { get; }

            public GameObject Object { get; }

            public int CompareTo(PathObject other)
            {
                int comparision = Z - other.Z;

                if (comparision == 0)
                {
                    comparision = Height - other.Height;
                }

                return comparision;
            }
        }
    }
}