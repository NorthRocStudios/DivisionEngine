//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using ComputeSharp;

namespace DivisionEngine.Rendering.ShaderUtilities
{
    /// <summary>
    /// Special math functions for shaders exclusively.
    /// </summary>
    public static class ShaderMath
    {
        /// <summary>
        /// Picks a random color from an integer.
        /// </summary>
        /// <param name="id">Object ID</param>
        /// <returns>Random hashed color</returns>
        public static float3 IntToColor(uint id)
        {
            // Mix the bits using prime numbers
            uint hash = id;
            hash ^= hash >> 16;
            hash *= 0x85ebca6b;
            hash ^= hash >> 13;
            hash *= 0xc2b2ae35;
            hash ^= hash >> 16;

            // Convert to float in [0,1] range
            float r = (hash & 0xFF) / 255.0f;
            float g = ((hash >> 8) & 0xFF) / 255.0f;
            float b = ((hash >> 16) & 0xFF) / 255.0f;

            // Ensure minimum brightness and saturation
            return Hlsl.Max(new float3(r, g, b), 0.2f);
        }

        public static float AdaptiveEpsilon(float td, float width, float height, float camScreenDist, float EPSILON)
        {
            float pixelSize = Hlsl.Max(td * camScreenDist / height, td * camScreenDist / width);
            return Hlsl.Max(EPSILON, pixelSize); // Could use half pixel size
        }

        // Quaternion rotation
        public static float3 RotateVector(float3 v, float4 r)
        {
            float3 qv = r.XYZ;
            float3 t = 2.0f * Hlsl.Cross(qv, v);
            return v + r.W * t + Hlsl.Cross(qv, t);
        }

        public static float3 InverseRotateVector(float3 v, float4 r)
        {
            // Conjugate of a unit quaternion is its inverse
            return RotateVector(v, new float4(-r.X, -r.Y, -r.Z, r.W));
        }

        public static float3 GetCameraRayDirNew(float aspect, float2 uv, float camScreenDist, float3 camForward, float3 camRight, float3 camUp)
        {
            float px = uv.X * aspect * camScreenDist;
            float py = uv.Y * camScreenDist;
            float3 rayDir = camForward + camRight * px + camUp * py;
            return Hlsl.Normalize(rayDir);
        }

        /// <summary>
        /// Unpacks a uint RGBA pixel to float4.
        /// </summary>
        public static float4 UnpackRGBA(uint packed)
        {
            float r = ((packed >> 24) & 0xFF) / 255.0f;
            float g = ((packed >> 16) & 0xFF) / 255.0f;
            float b = ((packed >> 8) & 0xFF) / 255.0f;
            float a = (packed & 0xFF) / 255.0f;
            return new float4(r, g, b, a);
        }

        public static float3 CalcBitangent(float3 normal, float3 tangent)
        {
            return Hlsl.Normalize(Hlsl.Cross(normal, tangent));
        }

        public static float3 WorldToLocalPoint(float3 worldPoint, float3 position, float4 rotation, float3 scaling)
        {
            float3 p = worldPoint - position;
            // Rotation is local->world, so going world->local needs the inverse.
            p = InverseRotateVector(p, rotation); // matches WorldSDF's convention exactly
            p *= scaling;                   // scaling = 1/worldScale, so textures stay fixed to the primitive's own UV space regardless of object scale
            return p;
        }

        public static float3 WorldToLocalDirection(float3 worldDir, float4 rotation)
        {
            return Hlsl.Normalize(InverseRotateVector(worldDir, rotation));
        }

        public static float3 LocalToWorldDirection(float3 localDir, float4 rotation)
        {
            // Inverse of WorldToLocalDirection, needed to bring a locally-perturbed
            // normal (e.g. from a normal map) back into world space
            return Hlsl.Normalize(RotateVector(localDir, rotation));
        }

        /// <summary>
        /// Calculates the luminance (perceived brightness) of an RGB color.
        /// Uses Rec. 709 / ITU-R BT.709 coefficients for sRGB color space.
        /// </summary>
        /// <param name="rgb">RGB color in linear or gamma space</param>
        /// <returns>Perceived luminance in [0, 1] range</returns>
        public static float Luminance(float3 rgb)
        {
            return Hlsl.Dot(rgb, new float3(0.2126f, 0.7152f, 0.0722f));
        }

        /// <summary>
        /// Calculates the luminance of an RGB color stored in a float4.
        /// </summary>
        public static float Luminance(float4 color)
        {
            return Hlsl.Dot(color.RGB, new float3(0.2126f, 0.7152f, 0.0722f));
        }

        /// <summary>
        /// Calculates luminance using BT.2020 coefficients for HDR/wide color gamut.
        /// </summary>
        public static float LuminanceBT2020(float3 rgb)
        {
            return Hlsl.Dot(rgb, new float3(0.2627f, 0.6780f, 0.0593f));
        }
    }
}
