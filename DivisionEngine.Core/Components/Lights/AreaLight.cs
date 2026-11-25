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
    /// An area light: a spherical or rectangular emitter with physically sized soft
    /// shadows. Direct illumination is integrated over the light's extent and the
    /// visibility term comes from the minimal tangent cone search of Bán, Bálint and
    /// Valasek, "Area Lights in Signed Distance Function Scenes" (Eurographics 2019
    /// short paper).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="intensity"/> uses the same convention as <see cref="PointLight"/>:
    /// it is the irradiance the light delivers at one world unit, so an area light
    /// and a point light of equal intensity and colour light a surface equally. The
    /// emitter's size only changes how soft its shadows are (and how the highlight is
    /// shaped), never the overall brightness, which keeps mixed light rigs predictable.
    /// </para>
    /// <para>
    /// A rectangle light emits from the side its transform's forward axis (local -Z,
    /// the same axis a <see cref="DirectionalLight"/> points along) comes from: rotate
    /// the entity so that -Z points at the surface you want lit. Sphere lights are
    /// omnidirectional.
    /// </para>
    /// </remarks>
    public class AreaLight : IComponent
    {
        /// <summary>
        /// Emitter shape.
        /// </summary>
        public enum LightShape
        {
            /// <summary>Spherical emitter with the paper's fast closed-form shadow.</summary>
            Sphere = 0,
            /// <summary>Rectangular emitter with the paper's numerical integration.</summary>
            Rectangle = 1,
        }

        [Header("Light")]
        [Tooltip("Light color")]
        [Color(false)] public float4 color = ColorPalette.White;
        [Tooltip("Light intensity (same convention as a point light)")]
        public float intensity = 4f;
        [Tooltip("Emitter shape: Sphere or Rectangle")]
        public LightShape shape = LightShape.Sphere;

        [Header("Shape")]
        [Tooltip("Radius of a spherical emitter")]
        public float radius = 1.5f;
        [Tooltip("Width and height of a rectangular emitter")]
        public float2 size = new float2(4f, 2f);
        [Tooltip("Distance at which the light reaches zero; 0 = no cutoff")]
        public float range = 50f;

        [Header("Area Shadows")]
        [Tooltip("Samples per pixel for rectangular emitters (sphere lights are closed form)")]
        [Range(1, 64)] public int samples = 16;
        [Tooltip("Cone search accuracy: smaller is more accurate, larger is faster")]
        [Range(0.02f, 0.5f)] public float epsilon = 0.05f;

        public IComponent Clone() => new AreaLight
        {
            color = color,
            intensity = intensity,
            shape = shape,
            radius = radius,
            size = size,
            range = range,
            samples = samples,
            epsilon = epsilon,
        };
    }
}
