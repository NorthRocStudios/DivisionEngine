//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering.Terrain;

namespace DivisionEngine.Rendering.ShaderUtilities
{
    /// <summary>
    /// Functions for getting distances to different SDF primitive objects for shaders exclusively.
    /// </summary>
    public static class SDFPrimitives
    {
        public static float Sphere(float3 pt, float r)
        {
            //float3 s = new float3(8, 8, 8);
            //float3 l = new float3(100, 1, 100);
            //float3 q = pt - s * math.clamp(math.round(pt / s), -l, l);
            return math.length(pt) - r;
        }

        public static float Box(float3 pt, float3 size)
        {
            float3 q = math.abs(pt) - size;
            return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);

            //float3 s = new float3(8, 8, 8);
            //float3 l = new float3(10000, 6, 10000);
            //float3 q = pt - s * math.clamp(math.round(pt / s), -l, l);

            //float sd1 = TorusSDF(q, size.XY);
            //float sd2 = PyramidSDF(q, size.X);

            //return math.lerp(sd1, sd2, math.saturate(worldData.fogAnisotropy));
        }

        public static float RoundedBox(float3 pt, float3 size, float r)
        {
            float3 q = math.abs(pt) - size + r;
            return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f) - r;
        }

        public static float Torus(float3 pt, float2 tr)
        {
            float2 q = new float2(math.length(pt.xz) - tr.x, pt.y);
            return math.length(q) - tr.y;
        }

        public static float Pyramid(float3 pt, float h)
        {
            float m2 = h * h + 0.25f;

            pt.x = math.abs(pt.x);
            pt.z = math.abs(pt.z);
            if (pt.z > pt.x)
            {
                float swapXZ = pt.x;
                pt.x = pt.z;
                pt.z = swapXZ;
            }
            pt.x -= 0.5f;
            pt.z -= 0.5f;

            float3 q = new float3(pt.z, h * pt.y - 0.5f * pt.x, h * pt.x + 0.5f * pt.y);

            float s = math.max(-q.x, 0.0f);
            float t = math.saturate((q.y - 0.5f * pt.z) / (m2 + 0.25f));

            float a = m2 * (q.x + s) * (q.x + s) + q.y * q.y;
            float b = m2 * (q.x + 0.5f * t) * (q.x + 0.5f * t) + (q.y - m2 * t) * (q.y - m2 * t);

            float d2 = math.min(q.y, -q.x * m2 - q.y * 0.5f) > 0.0f ? 0.0f : math.min(a, b);

            return math.sqrt((d2 + q.z * q.z) / m2) * math.sign(math.max(q.z, -pt.y));
        }

        public static float Plane(float3 pt, float3 n, float h)
        {
            return math.dot(pt, math.normalize(n)) + h;
        }

        // Bound not exact, for performance
        public static float Cone(float3 pt, float2 c, float h)
        {
            float q = math.length(pt.xz);
            return math.max(math.dot(c.xy, new float2(q, pt.y)), -h - pt.y);
        }

        // Vertical version, for performance
        public static float Cylinder(float3 pt, float r, float h)
        {
            float2 d = math.abs(new float2(math.length(pt.xz), pt.y)) - new float2(r, h);
            return math.min(math.max(d.x, d.y), 0.0f) + math.length(math.max(d, 0.0f));
        }

        // Vertical version, for performance
        public static float Capsule(float3 pt, float r, float h)
        {
            pt.y -= math.clamp(pt.y, 0.0f, h);
            return math.length(pt) - r;
        }

        #region terrain

        /// <summary>
        /// Tapered capsule SDF with bend - ideal for grass blades
        /// </summary>
        public static float TaperedCapsuleBend(float3 pt, float rBottom, float rTop, float height, float bendAmount, float2 bendDirection)
        {
            // Bend the point
            float bendFactor = math.saturate(pt.y / height);
            float bendScale = bendFactor * bendFactor;
            float3 bentPt = pt;
            bentPt.x += bendScale * bendAmount * bendDirection.x;
            bentPt.z += bendScale * bendAmount * bendDirection.y;

            // Tapered capsule
            float t = math.clamp(bentPt.y / height, 0.0f, 1.0f);
            float radius = math.lerp(rBottom, rTop, t);

            // Get distance to capsule centerline
            float2 horizontal = new float2(bentPt.x, bentPt.z);
            float dist = math.length(horizontal) - radius;

            // Add vertical clipping
            float vertical = math.abs(bentPt.y - height * 0.5f) - height * 0.5f;

            return math.max(dist, vertical);
        }

        public static float Terrain(float3 pt, float size, float heightScale, float persistence, float lacunarity, int octaves)
        {
            float height = GradientNoise.FBMNoised(pt.xz / size, octaves, persistence, lacunarity, out float2 deriv);
            float terrainHeight = height * heightScale - (heightScale / 2f);

            //float slope = math.length(deriv) * heightScale / size;

            float terrain = pt.y - terrainHeight;
            return terrain;
        }

        public static float TerrainEroded(float3 pt, float size, float heightScale, float baseGain, float lacunarity,
            float erosionStrength, float gullyWeight, float erosionDetail, float erosionScale, int erosionOctaves,
            float erosionLacunarity, float erosionGain, float cellScale, float normalization, int baseOctaves, float4 rounding)
        {
            // Base terrain using FBM
            float2 uv = pt.xz / size;

            // Calculate base height using the noised function
            float3 baseTerrain = TerrainRendering.FractalNoiseWithDerivatives(uv, 3.0f, baseOctaves, lacunarity, baseGain);
            float baseHeightNorm = baseTerrain.x * 0.5f + 0.5f;
            float2 slope = new float2(baseTerrain.y, baseTerrain.z);

            // Erosion filter
            float fadeTarget = math.clamp(baseTerrain.x / 0.6f, -1.0f, 1.0f);
            float4 onset = new float4(1.25f, 1.25f, 2.8f, 1.5f);
            float2 assumedSlope = new float2(0.7f, 1.0f);

            float ridgeMap, debug;
            float3 heightSlope = new float3(baseHeightNorm, slope.x, slope.y);
            float4 erosionResult = TerrainRendering.ErosionFilter(
                uv, heightSlope, fadeTarget,
                erosionStrength, gullyWeight, erosionDetail,
                rounding, onset, assumedSlope,
                erosionScale, erosionOctaves, erosionLacunarity,
                erosionGain, cellScale, normalization,
                out ridgeMap, out debug);

            // Apply height offset
            float heightOffset = -0.65f;
            float offset = heightOffset * erosionResult.w;
            float erodedHeightNorm = baseHeightNorm + erosionResult.x + offset;
            float erodedHeight = (erodedHeightNorm - 0.5f) * heightScale;

            return pt.y - erodedHeight;
        }

        /// <summary>
        /// Combines terrain with grass blades using repeating SDFs
        /// </summary>
        public static float TerrainWithGrassBaked(float terrainHeight, float3 pt,
            float grassDensity, float grassHeight, float grassRadius, float bendAmount,
            int terrainIndex, float time, float2 windDirection, float windStrength)
        {
            float terrainDist = pt.y - terrainHeight;

            float margin = grassHeight * 0.6f + grassRadius * 2f;
            if (terrainDist > margin) return terrainDist;

            float2 worldXZ = pt.xz;
            float cellSize = 1.0f / grassDensity;
            float2 baseCellIndex = math.floor(worldXZ / cellSize);

            float grassDist = 1e8f;

            // 3x3 grid sampling for overlapping blades
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    float2 cellIndex = baseCellIndex + new float2(dx, dz);

                    // Jitter using cell index as seed
                    float2 jitter = new float2(
                        SimplexNoise.Noise2D(cellIndex * 13.7f + new float2(0.5f, 0.5f)) * 2f - 1f,
                        SimplexNoise.Noise2D(cellIndex * 29.3f + new float2(0.5f, 0.5f)) * 2f - 1f
                    ) * cellSize * 0.4f;

                    float2 cellCenterWorld = (cellIndex + 0.5f) * cellSize + jitter;

                    // Random blade parameters
                    float randomHeight = SimplexNoise.Noise2D(cellIndex * 7.1f + new float2(0.3f, 0.7f)) * 0.5f + 0.5f;
                    float randomBend = SimplexNoise.Noise2D(cellIndex * 11.3f + new float2(0.7f, 0.3f)) * 0.5f + 0.5f;
                    float randomRadius = SimplexNoise.Noise2D(cellIndex * 19.7f + new float2(0.1f, 0.9f)) * 0.5f + 0.5f;
                    float randomRotation = SimplexNoise.Noise2D(cellIndex * 23.1f + new float2(0.9f, 0.1f)) * 6.28318530718f;

                    // Blade height at root position (sample terrain at blade root)
                    float bladeGroundHeight = terrainHeight; // Simplified

                    float2 offsetXZ = worldXZ - cellCenterWorld;
                    float s = math.sin(randomRotation);
                    float c = math.cos(randomRotation);
                    float2 rotatedXZ = new float2(
                        offsetXZ.x * c - offsetXZ.y * s,
                        offsetXZ.x * s + offsetXZ.y * c
                    );

                    float3 grassLocal = new float3(rotatedXZ.x, pt.y - bladeGroundHeight, rotatedXZ.y);

                    // Wind animation
                    float windPhase = SimplexNoise.Noise2D(cellIndex * 3.3f + new float2(0.7f, 0.3f)) * 6.28318530718f;
                    float windBend = math.sin(time * 1.6f + windPhase) * windStrength * 0.3f;

                    // Wind direction influence
                    float windDot = math.dot(windDirection, new float2(rotatedXZ.x, rotatedXZ.y));
                    float windInfluence = math.saturate(windDot * 0.5f + 0.5f);

                    float rBottom = grassRadius * (0.8f + randomRadius * 0.4f);
                    float rTop = grassRadius * 0.05f * randomRadius;
                    float h = grassHeight * (0.5f + randomHeight * 0.5f);
                    float bend = (bendAmount * randomBend * 0.6f + windBend) * windInfluence;

                    // Use the fixed tapered capsule
                    float bladeDist = TaperedCapsuleBend(grassLocal, rBottom, rTop, h, bend, windDirection);
                    grassDist = math.min(grassDist, bladeDist);
                }
            }

            return math.min(terrainDist, grassDist);
        }


        #endregion terrain

        #region fractals

        /// <summary>
        /// Wraps each component of a vector into the range [0, period).
        /// </summary>
        /// <param name="x">Input vector</param>
        /// <param name="period">Wrap period</param>
        /// <returns>Wrapped vector</returns>
        public static float3 Mod(float3 x, float period)
        {
            return x - period * math.floor(x / period);
        }

        /// <summary>
        /// Distance estimator for the Mandelbulb, the 3D analogue of the Mandelbrot set.
        /// Uses the classic triplex power formula with derivative tracking.
        /// </summary>
        /// <param name="pt">Point in fractal local space</param>
        /// <param name="power">Triplex exponent (8 is the classic bulb)</param>
        /// <param name="iterations">Number of iterations</param>
        /// <param name="bailout">Escape radius</param>
        /// <returns>Conservative distance to the fractal surface</returns>
        public static float Mandelbulb(float3 pt, float power, int iterations, float bailout)
        {
            float3 z = pt;
            float dr = 1f;
            float r = 0f;
            float escape = math.max(bailout, 1.1f);
            int steps = iterations < 1 ? 1 : iterations;

            for (int i = 0; i < steps; i++)
            {
                r = math.length(z);
                if (r > escape) break;

                float safeR = math.max(r, 1e-6f);
                float theta = math.acos(math.max(-1f, math.min(1f, z.z / safeR)));
                float phi = math.atan2(z.y, z.x);

                float radius = math.pow(safeR, power);
                dr = math.pow(safeR, power - 1f) * power * dr + 1f;

                theta *= power;
                phi *= power;

                float sinTheta = math.sin(theta);
                z = radius * new float3(sinTheta * math.cos(phi), sinTheta * math.sin(phi), math.cos(theta)) + pt;
            }

            return 0.5f * math.log(math.max(r, 1e-6f)) * r / math.max(dr, 1e-6f);
        }

        /// <summary>
        /// Distance estimator for the Menger sponge: a cube with square holes
        /// recursively punched through every face.
        /// </summary>
        /// <param name="pt">Point in fractal local space</param>
        /// <param name="size">Half extent of the containing cube</param>
        /// <param name="holeScale">Recursive hole scale, usually 3</param>
        /// <param name="iterations">Number of subdivision levels</param>
        /// <returns>Conservative distance to the fractal surface</returns>
        public static float MengerSponge(float3 pt, float size, float holeScale, int iterations)
        {
            float halfExtent = math.max(size, 0.05f);
            float3 q = pt / halfExtent;
            float d = Box(q, new float3(1f, 1f, 1f));
            float s = 1f;
            float scale = math.max(holeScale, 2f);
            int steps = iterations < 0 ? 0 : iterations;

            for (int i = 0; i < steps; i++)
            {
                float3 a = Mod(q * s, 2f) - 1f;
                s *= scale;
                float3 fold = math.abs(1f - 3f * math.abs(a));
                float da = math.max(fold.x, fold.y);
                float db = math.max(fold.y, fold.z);
                float dc = math.max(fold.z, fold.x);
                float c = (math.min(da, math.min(db, dc)) - 1f) / s;
                d = math.max(d, c);
            }

            return d * halfExtent;
        }

        /// <summary>
        /// Distance estimator for the Mandelbox: a box/sphere folding fractal.
        /// </summary>
        /// <param name="pt">Point in fractal local space</param>
        /// <param name="scale">Fold scale, usually around 2</param>
        /// <param name="minRadius">Radius of the inner sphere fold</param>
        /// <param name="foldLimit">Box fold limit</param>
        /// <param name="iterations">Number of iterations</param>
        /// <returns>Conservative distance to the fractal surface</returns>
        public static float Mandelbox(float3 pt, float scale, float minRadius, float foldLimit, int iterations)
        {
            float limit = math.max(foldLimit, 0.1f);
            // A fold scale near +/-1 collapses the estimator; fall back to the classic value.
            float safeScale = math.abs(scale) < 1.1f ? -1.5f : scale;
            float minR = math.min(math.max(minRadius, 1e-3f), limit);
            float minR2 = minR * minR;
            float limit2 = limit * limit;
            float3 bound = new float3(limit, limit, limit);
            float3 z = pt;
            float dr = 1f;
            int steps = iterations < 1 ? 1 : iterations;

            for (int i = 0; i < steps; i++)
            {
                // Box fold
                z = math.max(math.min(z, bound), -bound) * 2f - z;

                // Sphere fold
                float r2 = math.dot(z, z);
                if (r2 < minR2)
                {
                    float t = limit2 / minR2;
                    z = z * t;
                    dr = dr * t;
                }
                else if (r2 < limit2)
                {
                    float t = limit2 / math.max(r2, 1e-9f);
                    z = z * t;
                    dr = dr * t;
                }

                z = z * safeScale + pt;
                dr = dr * math.abs(safeScale) + 1f;
            }

            return math.length(z) / math.max(math.abs(dr), 1e-6f);
        }

        #endregion fractals
    }
}

