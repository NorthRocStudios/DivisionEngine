//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Numerics;

namespace DivisionEngine.MathUtilities
{
    /// <summary>
    /// Extension methods for float3 vector operations.
    /// </summary>
    public static class Vector
    {
        // Add vectors
        public static float2 Add(this float2 a, float2 b) => new float2(a.x + b.x, a.y + b.y);
        public static float3 Add(this float3 a, float3 b) => new float3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static float4 Add(this float4 a, float4 b) => new float4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

        public static float2 Add(this float2 a, float b) => new float2(a.x + b, a.y + b);
        public static float3 Add(this float3 a, float b) => new float3(a.x + b, a.y + b, a.z + b);
        public static float4 Add(this float4 a, float b) => new float4(a.x + b, a.y + b, a.z + b, a.w + b);

        // Subtract vectors
        public static float2 Subtract(this float2 a, float2 b) => new float2(a.x - b.x, a.y - b.y);
        public static float3 Subtract(this float3 a, float3 b) => new float3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static float4 Subtract(this float4 a, float4 b) => new float4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

        public static float2 Subtract(this float2 a, float b) => new float2(a.x - b, a.y - b);
        public static float3 Subtract(this float3 a, float b) => new float3(a.x - b, a.y - b, a.z - b);
        public static float4 Subtract(this float4 a, float b) => new float4(a.x - b, a.y - b, a.z - b, a.w - b);

        // Multiply vectors
        public static float2 Multiply(this float2 a, float2 b) => new float2(a.x * b.x, a.y * b.y);
        public static float3 Multiply(this float3 a, float3 b) => new float3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float4 Multiply(this float4 a, float4 b) => new float4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);

        public static float2 Multiply(this float2 a, float b) => new float2(a.x * b, a.y * b);
        public static float3 Multiply(this float3 a, float b) => new float3(a.x * b, a.y * b, a.z * b);
        public static float4 Multiply(this float4 a, float b) => new float4(a.x * b, a.y * b, a.z * b, a.w * b);

        // Divide vectors
        public static float2 Divide(this float2 a, float2 b) => new float2(a.x / b.x, a.y / b.y);
        public static float3 Divide(this float3 a, float3 b) => new float3(a.x / b.x, a.y / b.y, a.z / b.z);
        public static float4 Divide(this float4 a, float4 b) => new float4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);

        public static float2 Divide(this float2 a, float b) => new float2(a.x / b, a.y / b);
        public static float3 Divide(this float3 a, float b) => new float3(a.x / b, a.y / b, a.z / b);
        public static float4 Divide(this float4 a, float b) => new float4(a.x / b, a.y / b, a.z / b, a.w / b);

        /// <summary>
        /// Calculates the dot product of two float2 vectors.
        /// </summary>
        /// <param name="a">Vector a</param>
        /// <param name="b">Vector b</param>
        /// <returns>Dot product of vectors a and b</returns>
        public static float Dot(this float2 a, float2 b) => a.x * b.x + a.y * b.y;

        /// <summary>
        /// Calculates the dot product of two float3 vectors.
        /// </summary>
        /// <param name="a">Vector a</param>
        /// <param name="b">Vector b</param>
        /// <returns>Dot product of vectors a and b</returns>
        public static float Dot(this float3 a, float3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        /// <summary>
        /// Calculates the dot product between two float4 vectors.
        /// </summary>
        /// <param name="q">Left vectors</param>
        /// <param name="p">Right vectors</param>
        /// <returns>Dot product of two float4 vectors</returns>
        public static float Dot(this float4 q, float4 p) => q.x * p.x + q.y * p.y + q.z * p.z + q.w * p.w;

        /// <summary>
        /// Computes the length (magnitude) of a the vector v.
        /// </summary>
        /// <param name="v">Magnitude vector</param>
        /// <returns>Magnitude of vector v</returns>
        public static float Length(this float2 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y);

        /// <summary>
        /// Computes the length (magnitude) of a the vector v.
        /// </summary>
        /// <param name="v">Magnitude vector</param>
        /// <returns>Magnitude of vector v</returns>
        public static float Length(this float3 v) => (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);

        /// <summary>
        /// Computes distance between two float2 vectors.
        /// </summary>
        /// <param name="a">Vector a</param>
        /// <param name="b">Vector b</param>
        /// <returns>Distance between vectors a and b</returns>
        public static float Distance(this float2 a, float2 b) => Length(b - a);

        /// <summary>
        /// Computes distance between two float3 vectors.
        /// </summary>
        /// <param name="a">Vector a</param>
        /// <param name="b">Vector b</param>
        /// <returns>Distance between vectors a and b</returns>
        public static float Distance(this float3 a, float3 b) => Length(b - a);

        /// <summary>
        /// Computes the cross product between two float3 vectors.
        /// </summary>
        /// <param name="a">Left vector</param>
        /// <param name="b">Right vector</param>
        /// <returns>Cross product of vectors</returns>
        public static float3 Cross(this float3 a, float3 b) => new float3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x
        );

        /// <summary>
        /// Normalizes a float2 vector value.
        /// </summary>
        /// <param name="v">Vector to normalize</param>
        /// <returns>Normalized vector value</returns>
        public static float2 Normalize(this float2 v) => Vector2.Normalize(v.ToVector2()).ToFloat2();

        /// <summary>
        /// Normalizes a float3 vector value.
        /// </summary>
        /// <param name="v">Vector to normalize</param>
        /// <returns>Normalized vector value</returns>
        public static float3 Normalize(this float3 v) => Vector3.Normalize(v.ToVector3()).ToFloat3();

        /// <summary>
        /// Reflects a vector over a normal vector.
        /// </summary>
        /// <param name="I">Vector to reflect</param>
        /// <param name="N">Normal vector to reflect over</param>
        /// <returns>Reflection vector</returns>
        public static float3 Reflect(float3 I, float3 N) => Vector3.Reflect(I.ToVector3(), N.ToVector3()).ToFloat3();

        public static float3 Refract(float3 I, float3 N, float eta)
        {
            float cosi = Math.Clamp(Dot(I, N), -1, 1);
            float etai = 1, etat = eta;
            if (cosi < 0) cosi = -cosi;
            else
            {
                (etat, etai) = (etai, etat);
                N = -N;
            }
            float etaRatio = etai / etat;
            float k = 1 - etaRatio * etaRatio * (1 - cosi * cosi);
            if (k < 0) return new float3(0, 0, 0);
            else return etaRatio * I + (etaRatio * cosi - (float)Math.Sqrt(k)) * N;
        }

        public static float3 Lerp(float3 a, float3 b, float t) => Vector3.Lerp(a.ToVector3(), b.ToVector3(), t).ToFloat3();

        public static float3 Slerp(float3 a, float3 b, float t)
        {
            float dot = Dot(Normalize(a), Normalize(b));
            dot = Math.Clamp(dot, -1, 1);
            float theta = (float)Math.Acos(dot) * t;
            float3 relativeVec = Normalize(b - dot * a);
            return Normalize(a * (float)Math.Cos(theta) + relativeVec * (float)Math.Sin(theta));
        }

        public static float3 Transform(this float3 vector, float4x4 matrix) =>
            Vector3.Transform(vector.ToVector3(), matrix.ToMatrix4x4()).ToFloat3();

        public static float3 Transform(this float3 vector, float4 quaternion) =>
            Vector3.Transform(vector.ToVector3(), quaternion.ToQuaternion()).ToFloat3();

        /// <summary>
        /// Converts a float2 vector to a System.Numerics Vector2 value.
        /// </summary>
        /// <param name="vector">Vector to convert</param>
        /// <returns>Converted Vector2 value</returns>
        public static Vector2 ToVector2(this float2 vector) => new Vector2(vector.x, vector.y);

        /// <summary>
        /// Converts a System Vector2 value to a float2 vector.
        /// </summary>
        /// <param name="vector">Vector to convert</param>
        /// <returns>Converted float2 vector value</returns>
        public static float2 ToFloat2(this Vector2 vector) => new float2(vector.X, vector.Y);

        /// <summary>
        /// Converts a float3 vector to a System.Numerics Vector3 value.
        /// </summary>
        /// <param name="vector">Vector to convert</param>
        /// <returns>Converted Vector3 value</returns>
        public static Vector3 ToVector3(this float3 vector) => new Vector3(vector.x, vector.y, vector.z);

        /// <summary>
        /// Converts a System Vector3 value to a float3 vector.
        /// </summary>
        /// <param name="vector">Vector to convert</param>
        /// <returns>Converted float3 vector value</returns>
        public static float3 ToFloat3(this Vector3 vector) => new float3(vector.X, vector.Y, vector.Z);

        /// <summary>
        /// Converts a float4 vector to a System.Numerics Vector4 value.
        /// </summary>
        /// <param name="vector">Vector to convert</param>
        /// <returns>Converted Vector4 value</returns>
        public static Vector4 ToVector4(this float4 vector) => new Vector4(vector.x, vector.y, vector.z, vector.w);

        /// <summary>
        /// Converts a System Vector4 value to a float4 vector.
        /// </summary>
        /// <param name="vector">Vector to convert</param>
        /// <returns>Converted float4 vector value</returns>
        public static float4 ToFloat4(this Vector4 vector) => new float4(vector.X, vector.Y, vector.Z, vector.W);
    }
}
