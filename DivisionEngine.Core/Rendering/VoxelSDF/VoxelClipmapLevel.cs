//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.VoxelSDF
{
    /// <summary>
    /// One level of the SDF clipmap. Cells are addressed toroidally by absolute
    /// world cell index, so moving the active window does not move the voxels:
    /// only the newly exposed L-shaped shell is baked.
    /// </summary>
    public sealed class VoxelClipmapLevel : IDisposable
    {
        public VoxelClipmapLevel(int index, int resolution)
        {
            Index = index;
            Resolution = resolution;
            ClearReady();
        }

        public int Index { get; }
        public int Resolution { get; }

        /// <summary>World size of one cell at this level.</summary>
        public float CellSize { get; internal set; }

        /// <summary>Distance clamp used by the bake for this level.</summary>
        public float MaxDistance { get; internal set; }

        /// <summary>World cell bounds of the desired active window (inclusive).</summary>
        public int3 ActiveMinCell { get; internal set; }
        public int3 ActiveMaxCell { get; internal set; }

        /// <summary>World cell bounds guaranteed to be baked (inclusive).</summary>
        public int3 ReadyMinCell { get; internal set; }
        public int3 ReadyMaxCell { get; internal set; }

        /// <summary>Number of queued bake jobs that still need to finish this window.</summary>
        public int PendingJobs { get; internal set; }

        public bool HasReady =>
            ReadyMinCell.x <= ReadyMaxCell.x &&
            ReadyMinCell.y <= ReadyMaxCell.y &&
            ReadyMinCell.z <= ReadyMaxCell.z;

        public bool IsFullWindow =>
            HasReady &&
            ReadyMinCell.x == ActiveMinCell.x && ReadyMaxCell.x == ActiveMaxCell.x &&
            ReadyMinCell.y == ActiveMinCell.y && ReadyMaxCell.y == ActiveMaxCell.y &&
            ReadyMinCell.z == ActiveMinCell.z && ReadyMaxCell.z == ActiveMaxCell.z;

        public void ClearReady()
        {
            ReadyMinCell = new int3(1, 0, 0);
            ReadyMaxCell = new int3(0, 0, 0);
        }

        public bool ContainsReady(int3 cell) =>
            cell.x >= ReadyMinCell.x && cell.x <= ReadyMaxCell.x &&
            cell.y >= ReadyMinCell.y && cell.y <= ReadyMaxCell.y &&
            cell.z >= ReadyMinCell.z && cell.z <= ReadyMaxCell.z;

        public void Dispose()
        {
            // The renderer owns the shared atlas; a level only holds window state.
        }
    }
}
