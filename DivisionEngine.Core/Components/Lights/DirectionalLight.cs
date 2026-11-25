//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.Components.FieldAttributes;
using DivisionEngine.MathUtilities;

namespace DivisionEngine.Components.Lights
{
    /// <summary>
    /// Represents a directional light in the world.
    /// </summary>
    public class DirectionalLight : IComponent
    {
        [Color(false)] public float4 color = ColorPalette.White;
        public float intensity = 1f;

        public IComponent Clone() => new DirectionalLight
        {
            color = color,
            intensity = intensity,
        };
    }
}
