#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RCK.Social
{
    /// <summary>
    ///   The tiles each turf covers on the level's 160×160 tile grid, shared by the big-map tint (<see cref="TurfOverlay"/>)
    ///   and the commander's map (<see cref="CommandMap"/>). A turf covers the floor of its owner ID in its start chunk
    ///   (any owner's floor there for owner 99, as in <see cref="Territorial.OnTurf"/>); a turf with no owned floor (its
    ///   holders stand on the street) gets a small disc around each post. Rebuilt when the level or the turf list changes;
    ///   <see cref="Version"/> counts the rebuilds. Host only, like the turf list.
    /// </summary>
    internal static class TurfShapes
    {
        internal const int Size = 160;
        internal const float Tile = 0.64f;

        internal sealed class Shape
        {
            public TurfWar.Turf Turf;
            public int Index;
            /// <summary>Tile indexes (<c>y * Size + x</c>).</summary>
            public readonly List<int> Tiles = new List<int>();
            /// <summary>Per tile: true if a side faces another shape or open ground.</summary>
            public readonly List<bool> Edges = new List<bool>();
            /// <summary>The average of the posts, in world units.</summary>
            public Vector2 Centre;
        }

        private static readonly List<Shape> shapes = new List<Shape>();
        private static readonly int[] cell = new int[Size * Size];
        private static int stamp = int.MinValue;

        internal static IReadOnlyList<Shape> All => shapes;

        /// <summary>Goes up by one each time the shapes are rebuilt.</summary>
        internal static int Version { get; private set; }

        /// <summary>A normal level whose tiles can be read (not the home base, the tutorial or a streaming world).</summary>
        internal static bool LevelReady(GameController gc)
            => gc != null && !gc.streamingWorld && gc.levelType != "HomeBase" && gc.levelType != "Tutorial"
               && gc.tileInfo != null && gc.tileInfo.tileArray != null && gc.tileInfo.tilemapWalls != null;

        /// <summary>Builds the shapes if the level or the turf list changed. False if there's nothing to show.</summary>
        internal static bool Ensure(GameController gc)
        {
            IReadOnlyList<TurfWar.Turf> turfs = TurfWar.Turfs;
            if (!LevelReady(gc) || turfs.Count == 0) return false;
            if (!LevelScope.IsNew(ref stamp) && SameTurfs(turfs)) return true;
            Build(gc, turfs);
            Version++;
            return true;
        }

        /// <summary>The shape covering tile (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
        internal static Shape At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return null;
            int i = cell[y * Size + x];
            return i > 0 && i <= shapes.Count ? shapes[i - 1] : null;
        }

        /// <summary>True if one of <paramref name="t"/>'s holders is fighting.</summary>
        internal static bool Contested(TurfWar.Turf t)
        {
            if (t.Current == 0) return false;
            foreach (Agent h in t.Holders)
                if (h != null && !TurfWar.IsResolved(h) && h.inCombat) return true;
            return false;
        }

        internal static int ToTile(float world) => Mathf.RoundToInt(world / Tile);

        private static bool SameTurfs(IReadOnlyList<TurfWar.Turf> turfs)
        {
            if (turfs.Count != shapes.Count) return false;
            for (int i = 0; i < turfs.Count; i++)
                if (!ReferenceEquals(turfs[i], shapes[i].Turf)) return false;
            return true;
        }

        private static void Build(GameController gc, IReadOnlyList<TurfWar.Turf> turfs)
        {
            shapes.Clear();
            Array.Clear(cell, 0, cell.Length);
            var byGroup = new Dictionary<long, int>();
            var anyChunk = new Dictionary<int, int>();
            for (int i = 0; i < turfs.Count; i++)
            {
                TurfWar.Turf t = turfs[i];
                var s = new Shape { Turf = t, Index = i };
                if (t.Posts.Count > 0)
                {
                    Vector2 sum = Vector2.zero;
                    foreach (Vector2 p in t.Posts) sum += p;
                    s.Centre = sum / t.Posts.Count;
                }
                shapes.Add(s);
                if (t.Owner == 99) anyChunk[t.Chunk] = i;
                else byGroup[Group(t.Owner, t.Chunk)] = i;
            }

            TileData[,] tiles = gc.tileInfo.tileArray;
            tk2dTileMap walls = gc.tileInfo.tilemapWalls;
            int w = Math.Min(tiles.GetLength(0), Size), h = Math.Min(tiles.GetLength(1), Size);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    TileData td = tiles[x, y];
                    if (td == null || td.owner == 0 || td.wallSide != wallSideType.None) continue;
                    if (!byGroup.TryGetValue(Group(td.owner, td.chunkID), out int i) && !anyChunk.TryGetValue(td.chunkID, out i)) continue;
                    if (walls.GetTile(x, y, 0) != -1) continue;
                    int idx = y * Size + x;
                    cell[idx] = i + 1;
                    shapes[i].Tiles.Add(idx);
                }
            }

            // A turf with no owned floor (its holders stand on the street): a small disc around each post.
            for (int i = 0; i < shapes.Count; i++)
            {
                if (shapes[i].Tiles.Count > 0) continue;
                foreach (Vector2 post in turfs[i].Posts)
                {
                    int cx = ToTile(post.x), cy = ToTile(post.y);
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        for (int dy = -3; dy <= 3; dy++)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (dx * dx + dy * dy > 10 || x < 0 || y < 0 || x >= w || y >= h) continue;
                            int idx = y * Size + x;
                            if (cell[idx] != 0 || walls.GetTile(x, y, 0) != -1) continue;
                            cell[idx] = i + 1;
                            shapes[i].Tiles.Add(idx);
                        }
                    }
                }
            }

            foreach (Shape s in shapes)
            {
                int mine = s.Index + 1;
                foreach (int idx in s.Tiles)
                {
                    int x = idx % Size, y = idx / Size;
                    s.Edges.Add(Other(x - 1, y, mine) || Other(x + 1, y, mine) || Other(x, y - 1, mine) || Other(x, y + 1, mine));
                }
            }
        }

        private static long Group(int owner, int chunk) => ((long)owner << 32) | (uint)chunk;

        private static bool Other(int x, int y, int mine) => x < 0 || y < 0 || x >= Size || y >= Size || cell[y * Size + x] != mine;
    }
}
