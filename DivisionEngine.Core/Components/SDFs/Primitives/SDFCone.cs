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
    /// Represents a cone SDF.
    /// </summary>
    public class SDFCone : IComponent
    {
        public float2 cone = new float2(0.6f, 0.4f);
        public float height = 3f;

        public IComponent Clone() => new SDFCone
        {
            cone = cone,
            height = height,
        };
    }
}
