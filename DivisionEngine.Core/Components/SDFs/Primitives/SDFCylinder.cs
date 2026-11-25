//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
namespace DivisionEngine.Components.SDFs.Primitives
{
    /// <summary>
    /// Represents a cylinder SDF.
    /// </summary>
    public class SDFCylinder : IComponent
    {
        public float height = 3f;
        public float radius = 1f;

        public IComponent Clone() => new SDFCylinder
        {
            height = height,
            radius = radius,
        };
    }
}
