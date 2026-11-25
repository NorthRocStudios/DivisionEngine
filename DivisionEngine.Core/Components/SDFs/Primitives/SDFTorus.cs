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
    /// Represents a SDF donut shape.
    /// </summary>
    public class SDFTorus : IComponent
    {
        public float wholeRadius = 2f;
        public float ringRadius = 1f;

        public IComponent Clone() => new SDFTorus
        {
            wholeRadius = wholeRadius,
            ringRadius = ringRadius,
        };
    }
}
