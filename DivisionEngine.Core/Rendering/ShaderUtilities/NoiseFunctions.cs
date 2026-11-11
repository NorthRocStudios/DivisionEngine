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
    /// Gradient noise functions for shaders exclusively.
    /// </summary>
    public static class GradientNoise
    {
        public static float2 hash(float2 x)
        {
            float2 k = new float2(0.3183099f, 0.3678794f);
            x = x * k + new float2(k.y, k.x);
            return -1.0f + 2.0f * math.frac(16.0f * k * math.frac(x.x * x.y * (x.x + x.y)));
        }

        // Noise function with derivatives
        public static float3 noised(float2 x, int method, int interpolant)
        {
            float2 i;
            float2 f;

            if (method == 0)
            {
                int2 i_int = new int2((int)math.floor(x.x), (int)math.floor(x.y));
                i = new float2(i_int.x, i_int.y);
                f = math.frac(x);
            }
            else
            {
                i = math.floor(x);
                f = math.frac(x);
            }

            // Interpolation weights
            float2 u;
            float2 du;

            if (interpolant == 1)
            {
                // quintic interpolation
                u = f * f * f * (f * (f * 6.0f - 15.0f) + 10.0f);
                du = 30.0f * f * f * (f * (f - 2.0f) + 1.0f);
            }
            else
            {
                // cubic interpolation
                u = f * f * (3.0f - 2.0f * f);
                du = 6.0f * f * (1.0f - f);
            }

            float2 ga, gb, gc, gd;

            if (method == 0)
            {
                ga = hash(i + new float2(0.0f, 0.0f));
                gb = hash(i + new float2(1.0f, 0.0f));
                gc = hash(i + new float2(0.0f, 1.0f));
                gd = hash(i + new float2(1.0f, 1.0f));
            }
            else
            {
                ga = hash(i + new float2(0.0f, 0.0f));
                gb = hash(i + new float2(1.0f, 0.0f));
                gc = hash(i + new float2(0.0f, 1.0f));
                gd = hash(i + new float2(1.0f, 1.0f));
            }

            float va = math.dot(ga, f - new float2(0.0f, 0.0f));
            float vb = math.dot(gb, f - new float2(1.0f, 0.0f));
            float vc = math.dot(gc, f - new float2(0.0f, 1.0f));
            float vd = math.dot(gd, f - new float2(1.0f, 1.0f));

            // Value
            float value = va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd);

            // Derivatives
            float2 derivatives = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) +
                                 du * (new float2(u.y, u.x) * (va - vb - vc + vd) + new float2(vb, vc) - va);

            return new float3(value, derivatives.x, derivatives.y);
        }

        // Convenience wrapper with default parameters (method=1 for vec2, interpolant=1 for quintic)
        public static float3 noised(float2 x)
        {
            return noised(x, 1, 1);
        }

        // FBM (Fractal Brownian Motion) using the new noise function
        public static float FBMNoised(float2 x, int octaves, float persistence, float lacunarity, out float2 derivatives)
        {
            float value = 0.0f;
            derivatives = new float2(0f);
            float amplitude = 1.0f;
            float frequency = 1.0f;
            float maxAmp = 0.0f;

            for (int i = 0; i < octaves; i++)
            {
                float3 n = noised(x * frequency);
                value += amplitude * n.x;
                derivatives += amplitude * frequency * new float2(n.y, n.z);
                maxAmp += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            value /= maxAmp;
            derivatives /= maxAmp;
            return value;
        }
    }

    /// <summary>
    /// Simplex noise functions for 2D and 3D.
    /// Based on the original implementation by Ken Perlin and Stefan Gustavson.
    /// </summary>
    public static class SimplexNoise
    {
        // Skew and unskew factors
        private const float F2 = 0.5f * (1.7320508f - 1.0f); // (sqrt(3)-1)/2
        private const float G2 = (3.0f - 1.7320508f) / 6.0f; // (3-sqrt(3))/6
        private const float F3 = 1.0f / 3.0f;
        private const float G3 = 1.0f / 6.0f;

        /// <summary>
        /// Get gradient for 2D based on hash (no arrays)
        /// </summary>
        private static float2 Grad2(uint hash)
        {
            uint g = hash & 7;

            if (g == 0) return new float2(1, 1);
            if (g == 1) return new float2(-1, 1);
            if (g == 2) return new float2(1, -1);
            if (g == 3) return new float2(-1, -1);
            if (g == 4) return new float2(1, 0);
            if (g == 5) return new float2(-1, 0);
            if (g == 6) return new float2(0, 1);
            return new float2(0, -1);
        }

        /// <summary>
        /// Get gradient for 3D based on hash (no arrays)
        /// </summary>
        private static float3 Grad3(uint hash)
        {
            uint g = hash & 15;
            float3 grad = new float3(0f);

            // x component
            if (g < 8) grad.x = 1.0f;
            else grad.x = -1.0f;

            // y component
            if (g < 4) grad.y = 1.0f;
            else if (g < 12) grad.y = -1.0f;
            else grad.y = 0.0f;

            // z component
            if ((g & 1) != 0) grad.z = 1.0f;
            else if ((g & 2) != 0) grad.z = -1.0f;
            else grad.z = 0.0f;

            return grad;
        }

        /// <summary>
        /// Fast hash function for consistent pseudo-random values
        /// </summary>
        private static uint Hash(uint x)
        {
            x = (x ^ 61) ^ (x >> 16);
            x = x + (x << 3);
            x = x ^ (x >> 4);
            x = x * 0x27d4eb2d;
            x = x ^ (x >> 15);
            return x;
        }

        /// <summary>
        /// 2D hash from two integers
        /// </summary>
        private static uint Hash2(int2 p)
        {
            return Hash(Hash((uint)p.x) ^ Hash((uint)p.y));
        }

        /// <summary>
        /// 3D hash from three integers
        /// </summary>
        private static uint Hash3(int3 p)
        {
            return Hash(Hash(Hash((uint)p.x) ^ Hash((uint)p.y)) ^ Hash((uint)p.z));
        }

        /// <summary>
        /// Falloff function - t^4 for smooth blending
        /// </summary>
        private static float Falloff(float t2)
        {
            if (t2 < 0) return 0;
            float t = t2 * t2;
            return t * t;
        }

        // ============================================================
        // 2D SIMPLEX NOISE
        // ============================================================

        /// <summary>
        /// 2D Simplex noise, returns value in range [0, 1]
        /// </summary>
        public static float Noise2D(float2 p)
        {
            // Step 1: Skew to triangular grid
            float s = (p.x + p.y) * F2;
            float2 skew = p + s;

            // Step 2: Find cell coordinates
            int2 i = new int2((int)math.floor(skew).x, (int)math.floor(skew).y);
            float2 f = skew - new float2(i.x, i.y);

            // Step 3: Determine which triangle we're in (lower or upper)
            int2 ijk;
            if (f.x > f.y)
                ijk = new int2(1, 0);
            else
                ijk = new int2(0, 1);

            // Step 4: Get vertices (unskewed)
            float t = (i.x + i.y) * G2;
            float2 p0 = new float2(i.x, i.y) - t;

            t = (i.x + ijk.x + i.y + ijk.y) * G2;
            float2 p1 = new float2((i + ijk).x, (i + ijk).y) - t;

            t = (i.x + 1 + i.y + 1) * G2;
            float2 p2 = new float2((i + 1).x, (i + 1).y) - t;

            // Step 5: Vectors from vertices to point
            float2 d0 = p - p0;
            float2 d1 = p - p1;
            float2 d2 = p - p2;

            // Step 6: Get gradients
            uint hash0 = Hash2(i);
            uint hash1 = Hash2(i + ijk);
            uint hash2 = Hash2(i + new int2(1, 1));

            float2 grad0 = Grad2(hash0 % 12);
            float2 grad1 = Grad2(hash1 % 12);
            float2 grad2 = Grad2(hash2 % 12);

            // Step 7: Calculate contributions
            float t0 = Falloff(0.6f - math.dot(d0, d0));
            float t1 = Falloff(0.6f - math.dot(d1, d1));
            float t2 = Falloff(0.6f - math.dot(d2, d2));

            float n0 = t0 * math.dot(grad0, d0);
            float n1 = t1 * math.dot(grad1, d1);
            float n2 = t2 * math.dot(grad2, d2);

            // Step 8: Sum and map to [0,1].
            // The raw simplex sum with this falloff radius is roughly +/-0.04, so it needs the
            // same kind of gain the 3D version uses (16.0 there); 0.5 left the result pinned at
            // 0.5, which made every heightmap (and all grass jitter) constant.
            return (n0 + n1 + n2) * 12.4f + 0.5f;
        }

        // ============================================================
        // 3D SIMPLEX NOISE
        // ============================================================

        /// <summary>
        /// 3D Simplex noise, returns value in range [0, 1]
        /// </summary>
        public static float Noise3D(float3 p)
        {
            // Step 1: Skew to tetrahedral grid
            float s = (p.x + p.y + p.z) * F3;
            float3 skew = p + s;

            // Step 2: Find cell coordinates
            int3 i = new int3((int)math.floor(skew).x, (int)math.floor(skew).y, (int)math.floor(skew).z);
            float3 f = skew - new float3(i.x, i.y, i.z);

            // Step 3: Determine which tetrahedron we're in
            // Compare fractional parts to determine the ordering
            int3 ijk;
            if (f.x >= f.y)
            {
                if (f.y >= f.z)
                    ijk = new int3(1, 0, 0);
                else if (f.x >= f.z)
                    ijk = new int3(1, 0, 1);
                else
                    ijk = new int3(0, 0, 1);
            }
            else
            {
                if (f.y < f.z)
                    ijk = new int3(0, 1, 0);
                else if (f.x < f.z)
                    ijk = new int3(0, 1, 1);
                else
                    ijk = new int3(1, 1, 0);
            }

            // Step 4: Get all 4 vertices of the tetrahedron
            float unskew = (i.x + i.y + i.z) * G3;
            float3 p0 = new float3(i.x, i.y, i.z) - unskew;

            unskew = (i.x + ijk.x + i.y + ijk.y + i.z + ijk.z) * G3;
            float3 p1 = new float3((i + ijk).x, (i + ijk).y, (i + ijk).z) - unskew;

            int3 i2 = i + new int3(
                ijk.x == 0 ? 1 : 0,
                ijk.y == 0 ? 1 : 0,
                ijk.z == 0 ? 1 : 0
            );
            unskew = (i2.x + i2.y + i2.z) * G3;
            float3 p2 = new float3(i2.x, i2.y, i2.z) - unskew;

            int3 i3 = i + new int3(1, 1, 1);
            unskew = (i3.x + i3.y + i3.z) * G3;
            float3 p3 = new float3(i3.x, i3.y, i3.z) - unskew;

            // Step 5: Vectors from vertices to point
            float3 d0 = p - p0;
            float3 d1 = p - p1;
            float3 d2 = p - p2;
            float3 d3 = p - p3;

            // Step 6: Get gradients
            uint hash0 = Hash3(i);
            uint hash1 = Hash3(i + ijk);
            uint hash2 = Hash3(i2);
            uint hash3 = Hash3(i3);

            float3 grad0 = Grad3(hash0 % 16);
            float3 grad1 = Grad3(hash1 % 16);
            float3 grad2 = Grad3(hash2 % 16);
            float3 grad3 = Grad3(hash3 % 16);

            // Step 7: Calculate contributions with falloff
            float t0 = Falloff(0.6f - math.dot(d0, d0));
            float t1 = Falloff(0.6f - math.dot(d1, d1));
            float t2 = Falloff(0.6f - math.dot(d2, d2));
            float t3 = Falloff(0.6f - math.dot(d3, d3));

            float n0 = t0 * math.dot(grad0, d0);
            float n1 = t1 * math.dot(grad1, d1);
            float n2 = t2 * math.dot(grad2, d2);
            float n3 = t3 * math.dot(grad3, d3);

            // Step 8: Sum and map to [0,1]
            // The multiplier 32.0 normalizes the range to [-1,1]
            return (n0 + n1 + n2 + n3) * 16.0f + 0.5f;
        }

        // ============================================================
        // HELPER FUNCTIONS
        // ============================================================

        /// <summary>
        /// Fractal Brownian Motion - sums multiple octaves of noise
        /// </summary>
        public static float FBM2D(float2 p, int octaves, float persistence, float lacunarity)
        {
            float value = 0;
            float amplitude = 0.5f;
            float frequency = 1.0f;

            for (int i = 0; i < octaves; i++)
            {
                value += amplitude * Noise2D(p * frequency);
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return value;
        }

        /// <summary>
        /// Fractal Brownian Motion for 3D noise
        /// </summary>
        public static float FBM3D(float3 p, int octaves, float persistence, float lacunarity)
        {
            float value = 0;
            float amplitude = 0.5f;
            float frequency = 1.0f;

            for (int i = 0; i < octaves; i++)
            {
                value += amplitude * Noise3D(p * frequency);
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return value;
        }

        /// <summary>
        /// Domain-warped noise for more natural features
        /// </summary>
        public static float WarpedNoise3D(float3 p, int warpOctaves, float warpStrength)
        {
            // Generate warp vector using noise
            float3 warp = new float3(
                Noise3D(p + new float3(1000, 0, 0)) * 2 - 1,
                Noise3D(p + new float3(0, 1000, 0)) * 2 - 1,
                Noise3D(p + new float3(0, 0, 1000)) * 2 - 1
            ) * warpStrength;

            // Sample noise at warped position
            return Noise3D(p + warp);
        }

        /// <summary>
        /// Ridge noise (creates sharp ridges like mountains)
        /// </summary>
        public static float RidgeNoise3D(float3 p, int octaves)
        {
            float value = 0;
            float amplitude = 0.5f;
            float frequency = 1.0f;
            float weight = 1.0f;

            for (int i = 0; i < octaves; i++)
            {
                float noise = Noise3D(p * frequency);
                noise = 1.0f - math.abs(noise); // Create ridges
                noise *= noise * weight;
                weight = noise;

                value += noise * amplitude;
                amplitude *= 0.5f;
                frequency *= 2.0f;
            }

            return value;
        }
    }
}
