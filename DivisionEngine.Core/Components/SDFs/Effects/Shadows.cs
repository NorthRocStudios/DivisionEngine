//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components.FieldAttributes;

namespace DivisionEngine.Components.SDFs.Effects
{
    /// <summary>
    /// Allows SDF objects to cast and receive penumbra shadows.
    /// </summary>
    public class Shadows : IComponent
    {
        public enum ShadowType
        {
            Hard = 0, Soft = 1, Colored = 2, Caustic = 3,
        }

        /// <summary>
        /// This object contributes to shadow maps.
        /// </summary>
        [Header("Caster Settings")]
        [Tooltip("This object contributes to shadow maps")]
        public bool shadowCaster = true;
        /// <summary>
        /// World-space length of the penumbra.
        /// </summary>
        [Tooltip("World-space length of the penumbra")]
        public float penumbraDistance = 40f;
        /// <summary>
        /// Shadow ray near-plane.
        /// </summary>
        [Tooltip("Shadow ray near-plane")]
        public float minDistance = 0.001f;
        /// <summary>
        /// Shadow ray far-plane.
        /// </summary>
        [Tooltip("Shadow ray far-plane")]
        public float maxDistance = 100f;

        /// <summary>
        /// This object has a shadow map.
        /// </summary>
        [Header("Receiver Settings")]
        [Tooltip("This object has a shadow map")]
        public bool shadowReceiver = true;
        /// <summary>
        /// Type of shadow rendering.
        /// </summary>
        [Tooltip("Type of shadow rendering")]
        public ShadowType shadowStyle = ShadowType.Colored;

        public IComponent Clone() => new Shadows
        {
            shadowStyle = shadowStyle,
            shadowCaster = shadowCaster,
            shadowReceiver = shadowReceiver,

            penumbraDistance = penumbraDistance,
            minDistance = minDistance,
            maxDistance = maxDistance,
        };
    }
}
