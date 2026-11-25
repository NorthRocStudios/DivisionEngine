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
    /// Represents a box SDF.
    /// </summary>
    public class SDFBox : IComponent
    {
        public float3 size = new float3(1f, 1f, 1f);

        public IComponent Clone() => new SDFBox
        {
            size = size,
        };
    }
}
