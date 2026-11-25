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
    /// Represents a plane SDF.
    /// </summary>
    public class SDFPlane : IComponent
    {
        public float3 normal = new float3(0f, 1f, 0f);
        public float height = 1f;

        public IComponent Clone() => new SDFPlane
        {
            normal = normal,
            height = height,
        };
    }
}
