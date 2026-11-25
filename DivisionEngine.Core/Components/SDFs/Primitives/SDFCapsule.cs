//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Components.SDFs.Primitives
{
    /// <summary>
    /// Represents a capsule SDF.
    /// </summary>
    public class SDFCapsule : IComponent
    {
        public float height = 3f;
        public float radius = 1f;

        public IComponent Clone() => new SDFCapsule
        {
            height = height,
            radius = radius,
        };
    }
}
