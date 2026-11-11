//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.ShaderUtilities
{
    /// <summary>
    /// Functions for physically based rendering workflows in shaders.
    /// </summary>
    public static class PBR
    {
        /// <summary>
        /// Calculates fresnel reflectance with manual reflectance control and proper clamping.
        /// </summary>
        /// <param name="cosTheta">Cosine of the angle between normal and view direction</param>
        /// <param name="f0">Base Fresnel at normal incidence (0°)</param>
        /// <param name="reflectance">Manual reflectance multiplier (0 = no reflection, 1 = standard, >1 = enhanced)</param>
        /// <param name="f90">Optional grazing angle Fresnel value (default 1.0)</param>
        /// <returns>Clamped Fresnel factor in [0, 1] range</returns>
        public static float FresnelWithReflectance(float cosTheta, float f0, float reflectance, float f90)
        {
            // Clamp f0 to realistic values (minimum 2% reflection for dielectrics)
            float clampedF0 = math.clamp(f0, 0.02f, 1.0f);

            // Apply reflectance as a multiplier to control Fresnel strength
            // reflectance = 0 -> no Fresnel (fully transparent)
            // reflectance = 1 -> standard Fresnel
            // reflectance > 1 -> enhanced Fresnel (more reflective)
            float effectiveF0 = math.clamp(clampedF0 * reflectance, 0.02f, 1.0f);

            // Clamp f90 to reasonable range
            float effectiveF90 = math.clamp(f90 * reflectance, 0.02f, 1.0f);

            // Schlick approximation with f90 control
            float fresnel = effectiveF0 + (effectiveF90 - effectiveF0) * math.pow(1.0f - cosTheta, 5.0f);

            // Clamp to [0, 1] range and ensure we never get pure reflection or pure refraction
            return math.clamp(fresnel, 0.02f, 0.98f);
        }

        public static float3 FresnelSchlick(float cosTheta, float3 f0, float reflectance, float f90)
        {
            // Clamp f0 to realistic values (minimum 2% reflection for dielectrics)
            float3 clampedF0 = math.clamp(f0, 0.02f, 1.0f);

            // Apply reflectance as a multiplier to control Fresnel strength
            // reflectance = 0 -> no Fresnel (fully transparent)
            // reflectance = 1 -> standard Fresnel
            // reflectance > 1 -> enhanced Fresnel (more reflective)
            float3 effectiveF0 = math.clamp(clampedF0 * reflectance, 0.02f, 1.0f);

            // Clamp f90 to reasonable range
            float3 effectiveF90 = math.clamp(f90 * reflectance, 0.02f, 1.0f);

            // Schlick approximation with f90 control
            float3 fresnel = effectiveF0 + (effectiveF90 - effectiveF0) * math.pow(1.0f - cosTheta, 5.0f);

            // Clamp to [0, 1] range and ensure we never get pure reflection or pure refraction
            return math.clamp(fresnel, 0.02f, 0.98f);
        }

        /// <summary>
        /// Calculates the diffuse factor for GGX.
        /// </summary>
        /// <param name="NoH">Normal dot Halfway</param>
        /// <param name="alpha">Roughness squared</param>
        /// <returns>Diffuse GGX factor</returns>
        public static float D_GGX(float NoH, float alpha, float RECIPROCAL_PI)
        {
            float alpha2 = alpha * alpha;
            float NoH2 = NoH * NoH;
            float b = NoH2 * (alpha2 - 1f) + 1f;
            return alpha2 * RECIPROCAL_PI / (b * b);
        }

        /// <summary>
        /// Combines GGX Schlick functions for difference vectors.
        /// </summary>
        /// <param name="NoV">Normal dot View</param>
        /// <param name="NoL">Normal dot Light</param>
        /// <param name="alpha">Roughness squared</param>
        /// <returns>G Smith value</returns>
        public static float GSmith(float NoV, float NoL, float alpha, float EPSILON)
        {
            return G1_GGX_Schlick(NoL, alpha, EPSILON) * G1_GGX_Schlick(NoV, alpha, EPSILON);
        }

        /// <summary>
        /// Calculates the GGX Schlick function.
        /// </summary>
        /// <param name="NoV">Normal dot View</param>
        /// <param name="alpha">Roughness squared</param>
        /// <returns>G1 factor for GGX Schlick</returns>
        public static float G1_GGX_Schlick(float NoV, float alpha, float EPSILON)
        {
            float k = alpha / 2f;
            return math.max(NoV, EPSILON) / (NoV * (1f - k) + k);
        }

        /// <summary>
        /// Fresnel equation used by Disney.
        /// </summary>
        /// <param name="cosTheta">Cos theta angle</param>
        /// <param name="f0">Fresnel term</param>
        /// <param name="f90">90 degree fresnel term</param>
        /// <returns>Fresnel value</returns>
        public static float FresnelSchlick90(float cosTheta, float f0, float f90)
        {
            return f0 + (f90 - f0) * math.pow(1f - cosTheta, 5f);
        }

        /// <summary>
        /// Calculates the diffuse factor for the BRDF using Disney's method.
        /// </summary>
        /// <param name="NoV">Normal dot View</param>
        /// <param name="NoL">Normal dot Light</param>
        /// <param name="VoH">View dot Halfway</param>
        /// <param name="alpha">Roughness value squared</param>
        /// <returns>Disney diffuse factor for BRDF</returns>
        public static float DisneyDiffuseFactor(float NoV, float NoL, float VoH, float alpha)
        {
            float f90 = 0.5f + 2f * alpha * VoH * VoH;
            float F_In = FresnelSchlick90(NoL, 1f, f90);
            float F_Out = FresnelSchlick90(NoV, 1f, f90);
            return F_In * F_Out;
        }

        /// <summary>
        /// The bi-directional reflectance distributionfunction using Cook-Torrance.
        /// </summary>
        /// <param name="lightDir">Light direction</param>
        /// <param name="viewDir">View direction</param>
        /// <param name="finalNormal">Normal vector</param>
        /// <param name="sdf">SDF object to sample</param>
        /// <returns>BRDF output value</returns>
        public static float3 BRDFMicrofacetFunction(float3 lightDir, float3 viewDir, float3 finalNormal,
            float3 baseCol, float metallic, float roughAlpha, float specular, float reflectance, float RECIPROCAL_PI, float EPSILON)
        {
            float NoV = math.saturate(math.dot(finalNormal, viewDir));
            float NoL = math.saturate(math.dot(finalNormal, lightDir));
            float3 halfwayDir = math.normalize(viewDir + lightDir);
            float VoH = math.saturate(math.dot(viewDir, halfwayDir));
            float NoH = math.saturate(math.dot(finalNormal, halfwayDir));

            float3 f0 = new float3(1f) * 0.16f * specular * specular;
            f0 = math.lerp(f0, baseCol, new float3(metallic, metallic, metallic));

            float3 F = FresnelSchlick(VoH, f0, specular, 1f);
            float D = D_GGX(NoH, roughAlpha, RECIPROCAL_PI);
            float G = GSmith(NoV, NoL, roughAlpha, EPSILON);

            // Add epsilon to denominator to prevent division by near-zero
            float denominator = 4f * math.max(NoV * NoL, EPSILON);
            float3 specularTerm = math.min(F * D * G / denominator, 20f); // Clamp specular to prevent fireflies

            float3 rhoD = baseCol * DisneyDiffuseFactor(NoV, NoL, VoH, roughAlpha); // alternative: rhoD *= 1f - metallic;
            float3 diff = rhoD * RECIPROCAL_PI;

            // Hard cap to prevent explosions
            return math.min(diff + specularTerm, 100f);
        }

        // Reflections functions:

        public static uint HaltonHash(uint x)
        {
            x = x ^ 61 ^ (x >> 16);
            x += x << 3;
            x ^= x >> 4;
            x *= 0x27d4eb2d;
            x ^= x >> 15;
            return x;
        }

        // Halton sequence generator
        public static float HaltonSequence(int index, int baseNum)
        {
            float result = 0f;
            float f = 1f;
            int i = index;
            while (i > 0)
            {
                f /= baseNum;
                result += f * (i % baseNum);
                i /= baseNum;
            }
            return result;
        }

        // Generate 2D Halton sample
        public static float2 Halton2D(int index)
        {
            return new float2(HaltonSequence(index, 2), HaltonSequence(index, 3));
        }

        // Importance sample GGX distribution for specular reflections, alpha = roughness * roughness
        public static float3 ImportanceSampleGGX(float2 u, float3 normal, float alpha, float PI)
        {
            float phi = 2f * PI * u.x;
            float cosTheta = math.sqrt((1f - u.y) / (1f + (alpha * alpha - 1f) * u.y));
            float sinTheta = math.sqrt(1f - cosTheta * cosTheta);

            // Spherical to cartesian
            float3 h = new float3(math.cos(phi) * sinTheta, math.sin(phi) * sinTheta, cosTheta);

            // Tangent space to world space
            float3 up = math.abs(normal.z) < 0.999f ? new float3(0, 0, 1) : new float3(1, 0, 0);
            float3 tangent = math.normalize(math.cross(up, normal));
            float3 bitangent = math.cross(normal, tangent);
            return math.normalize(tangent * h.x + bitangent * h.y + normal * h.z);
        }
    }
}
