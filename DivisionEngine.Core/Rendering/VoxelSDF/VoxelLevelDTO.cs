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
    /// Per-level clipmap metadata consumed by the voxel render shader. Keeping this
    /// in one structured buffer avoids a long chain of per-level uniform branches.
    /// </summary>
    public struct VoxelLevelDTO
    {
        public float cellSize;
        public int full;
        public int3 minCell;
        public int3 maxCell;
    }
}
