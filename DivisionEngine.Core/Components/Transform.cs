//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
using DivisionEngine.Components.FieldAttributes;
using DivisionEngine.MathUtilities;

namespace DivisionEngine.Components
{
    /// <summary>
    /// Component for translation, rotation, and scaling.
    /// </summary>
    public class Transform : IComponent
    {
        /// <summary>
        /// Position of this entity.
        /// </summary>
        [Tooltip("Position of this entity")]
        public float3 position = new float3(0f, 0f, 0f);
        /// <summary>
        /// Rotation of this entity.
        /// </summary>
        [Tooltip("Rotation of this entity")]
        [Rotation(true)] public float4 rotation = Quaternion.Identity;
        /// <summary>
        /// Scale of this entity.
        /// </summary>
        [Tooltip("Scale of this entity")]
        public float3 scaling = new float3(1f, 1f, 1f);

        public float3 Forward => rotation.RotateVector(new float3(0, 0, -1)).Normalize();
        public float3 Back => rotation.RotateVector(new float3(0, 0, 1)).Normalize();
        public float3 Up => rotation.RotateVector(new float3(0, 1, 0)).Normalize();
        public float3 Down => rotation.RotateVector(new float3(0, -1, 0)).Normalize();
        public float3 Left => rotation.RotateVector(new float3(-1, 0, 0)).Normalize();
        public float3 Right => rotation.RotateVector(new float3(1, 0, 0)).Normalize();

        /*private static float3x3 RotAxis(float3 axis, float angle)
        {
            float s = Math.Sin(angle);
            float c = Math.Cos(angle);
            float oc = 1f - c;
            return new float3x3(
                oc * axis.X * axis.X + c, oc * axis.X * axis.Y - axis.Z * s, oc * axis.Z * axis.X + axis.Y * s,
                oc * axis.X * axis.Y + axis.Z * s, oc * axis.Y * axis.Y + c, oc * axis.Y * axis.Z - axis.X * s,
                oc * axis.Z * axis.X - axis.Y * s, oc * axis.Y * axis.Z + axis.X * s, oc * axis.Z * axis.Z + c
            );
        }*/

        public IComponent Clone() => new Transform
        {
            position = position,
            rotation = rotation,
            scaling = scaling,
        };
    }
}
