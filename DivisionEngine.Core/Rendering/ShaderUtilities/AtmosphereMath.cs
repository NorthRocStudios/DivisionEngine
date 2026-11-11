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
    /// Fast semi-physical Rayleigh/Mie/ozone atmospheric scattering. Produces a
    /// plausible sky gradient, horizon reddening, and aerial perspective without
    /// ray-marching the atmosphere itself - just a couple of sphere intersections
    /// and a curve-fit optical depth term.
    /// </summary>
    public static class AtmosphereMath
    {
        // Sane real-world (meter-scale) defaults for the Environment component.
        public const float DEFAULT_PLANET_RADIUS = 6371000f;
        public const float DEFAULT_ATMOSPHERE_HEIGHT = 100000f;

        private const float RAYLEIGH_MAX_LUM = 2.5f;
        private const float MIE_MAX_LUM = 0.5f;

        private const float M_EXPOSURE_MUL = 0.23f;   // Tuned to match a physical reference render
        private const float M_FAKE_MS = 0.3f;         // Cheap stand-in for multiple scattering
        private const float M_AERIAL = 2.5f;
        private const float M_TRANSMITTANCE = 0.25f;
        private const float M_LIGHT_TRANSMITTANCE = 1e6f;
        private const float M_DENSITY_HEIGHT_MOD = 1e-12f;
        private const float M_DENSITY_CAM_MOD = 10f;
        private const float M_OZONE = 1.5f;
        private const float M_OZONE2 = 5f;

        private static float Sq(float x)
        {
            return x * x;
        }

        private static float Pow4(float x)
        {
            return Sq(x) * Sq(x);
        }

        private static float Pow8(float x)
        {
            return Pow4(x) * Pow4(x);
        }

        /// <summary>
        /// Ray/sphere intersection. Returns (near, far) hit distances; negative
        /// values mean no hit or behind the ray origin.
        /// </summary>
        public static float2 SphereIntersection(float3 rayStart, float3 rayDir, float3 sphereCenter, float sphereRadius)
        {
            float3 oc = rayStart - sphereCenter;
            float b = math.dot(oc, rayDir);
            float c = math.dot(oc, oc) - sphereRadius * sphereRadius;
            float h = b * b - c;
            if (h < 0f) return new float2(-1f, -1f);
            h = math.sqrt(h);
            return new float2(-b - h, -b + h);
        }

        private static float PhaseR(float costh)
        {
            return (1f + costh * costh) * 0.06f;
        }

        private static float PhaseM(float costh, float g)
        {
            g = math.min(g, 0.9381f);
            float k = 1.55f * g - 0.55f * g * g * g;
            float a = 1f - k * k;
            float denomTerm = 1f - k * costh;
            float b = 12.57f * denomTerm * denomTerm;
            return a / b;
        }

        private static float3 GetLightTransmittance(float3 lightDir, float multiplier, float ozoneMultiplier, float density,
            float3 rayleighCoeff, float3 mieCoeff, float3 ozoneCoeff)
        {
            // Extra extinction as the light approaches/dips below the horizon
            float lightExtinctionAmount =
                math.exp(-(math.saturate(lightDir.y + 0.05f) * 40f)) +
                math.exp(-(math.saturate(lightDir.y + 0.5f) * 5f)) * 0.4f +
                Sq(math.saturate(1f - lightDir.y)) * 0.02f +
                0.002f;

            float3 extinction = rayleighCoeff + mieCoeff + ozoneCoeff * ozoneMultiplier;
            return math.exp(-extinction * lightExtinctionAmount * density * multiplier * M_LIGHT_TRANSMITTANCE);
        }

        private static void GetRayleighMie(float opticalDepth, float densityR, float densityM,
            float3 rayleighCoeff, float3 mieCoeff, out float3 rayleigh, out float3 mie)
        {
            rayleigh = (new float3(1f) - math.exp(-opticalDepth * densityR * rayleighCoeff / RAYLEIGH_MAX_LUM)) * RAYLEIGH_MAX_LUM;
            mie = (new float3(1f) - math.exp(-opticalDepth * densityM * mieCoeff / MIE_MAX_LUM)) * MIE_MAX_LUM;
        }

        /// <summary>
        /// Approximates in-scattered atmospheric light and view transmittance along a ray.
        /// Works for both an open sky (rayLength = a very large number) and, later, aerial
        /// perspective over real geometry (rayLength = surface hit distance).
        /// </summary>
        /// <param name="rayStart">World-space ray origin; Y is treated as height above sea level.</param>
        /// <param name="rayDir">Normalized view direction.</param>
        /// <param name="rayLength">Distance to the surface being viewed, or a large constant for open sky.</param>
        /// <param name="lightDir">Normalized direction FROM the surface TOWARD the sun.</param>
        /// <param name="lightColor">Sun color/radiance.</param>
        /// <param name="planetRadius">Planet radius, world units.</param>
        /// <param name="atmosphereHeight">Atmosphere shell thickness above the planet radius.</param>
        /// <param name="density">Overall atmosphere density multiplier.</param>
        /// <param name="mieAnisotropy">Mie phase forward-scattering term ("g"), roughly 0-0.9381.</param>
        /// <param name="aerialPerspectiveScale">How aggressively distance fades into haze.</param>
        /// <param name="transmittance">xyz = view transmittance, w = 1 if the ray reaches open sky, 0 if the planet occludes it.</param>
        /// <returns>In-scattered light color along the ray.</returns>
        public static float3 GetAtmosphere(
            float3 rayStart, float3 rayDir, float rayLength, float3 lightDir, float3 lightColor,
            float planetRadius, float atmosphereHeight, float density, float mieAnisotropy, float aerialPerspectiveScale,
            out float4 transmittance)
        {
            float3 rayleighCoeff = new float3(5.802e-6f, 13.558e-6f, 33.100e-6f);
            float3 mieCoeff = new float3(3.996e-6f, 3.996e-6f, 3.996e-6f);
            float3 ozoneCoeff = new float3(0.650e-6f, 1.881e-6f, 0.085e-6f);

            // Never let the camera dip below sea level - avoids NaNs in the planet intersection
            float3 origin = new float3(rayStart.x, math.max(rayStart.y, 1f), rayStart.z);

            float3 planetCenter = new float3(0f, -planetRadius, 0f);
            float2 tPlanet = SphereIntersection(origin, rayDir, planetCenter, planetRadius);
            float2 tAtmos = SphereIntersection(origin, rayDir, planetCenter, planetRadius + atmosphereHeight);

            float normAltitude = origin.y / atmosphereHeight;
            float atmosphereExit = tAtmos.y;

            if (atmosphereExit < 0f)
            {
                // Ray exits past the atmosphere shell entirely - nothing to scatter
                transmittance = new float4(1f, 1f, 1f, 1f);
                return new float3(0f);
            }

            // If the camera is above the shell, subtract the distance to entry
            atmosphereExit -= math.max(0f, tAtmos.x);

            float opticalDepth = tPlanet.x > 0f ? math.min(tPlanet.x, atmosphereExit) : atmosphereExit;
            opticalDepth = math.min(rayLength, opticalDepth);
            opticalDepth = math.min(opticalDepth * M_AERIAL * aerialPerspectiveScale, atmosphereExit);

            float hbias = 1f - 1f / (2f + Sq(atmosphereExit) * M_DENSITY_HEIGHT_MOD);
            hbias = math.pow(hbias, 1f + normAltitude * M_DENSITY_CAM_MOD);
            float sqhbias = Sq(hbias);
            float densityR = sqhbias * density;
            float densityM = Sq(sqhbias) * hbias * density;

            // Reddens/dims the light as the sun nears the horizon
            float ly = lightDir.y;
            ly += math.saturate(-lightDir.y + 0.02f) * math.saturate(lightDir.y + 0.7f);
            ly = math.clamp(ly, -1f, 1f);
            float3 horizonLightDir = new float3(lightDir.x, ly, lightDir.z);
            float3 tintedLightColor = lightColor *
                GetLightTransmittance(horizonLightDir, hbias, M_OZONE2, density, rayleighCoeff, mieCoeff, ozoneCoeff);

            GetRayleighMie(opticalDepth, densityR, densityM, rayleighCoeff, mieCoeff, out float3 rayleighScatter, out float3 mieScatter);

            float3 extinction = (rayleighCoeff * densityR + mieCoeff * densityM + ozoneCoeff * densityR * M_OZONE)
                * Pow4(1f - normAltitude) * M_TRANSMITTANCE;

            float costh = math.dot(rayDir, lightDir);
            float phaseR = PhaseR(costh);
            float phaseM = PhaseM(costh, mieAnisotropy);

            float3 rayleigh = (phaseR + phaseR * M_FAKE_MS) * tintedLightColor;
            float3 mie = (phaseM + phaseR * M_FAKE_MS) * tintedLightColor * new float3(0.95f, 0.85f, 0.75f);
            float3 scattering = mie * mieScatter + rayleigh * rayleighScatter;

            float3 viewTransmittance = math.exp(-(opticalDepth + Pow8(opticalDepth * 4.5e-6f)) * extinction);
            float planetHitFlag = tPlanet.x > 0f ? 0f : 1f;
            transmittance = new float4(viewTransmittance, planetHitFlag);

            return scattering * M_EXPOSURE_MUL;
        }

        /// <summary>
        /// A simple angular sun disc, softened at the edge. Mask with transmittance.w
        /// (from GetAtmosphere) so it doesn't punch through occluding geometry.
        /// </summary>
        private static float Hash13(float3 p)
        {
            p = math.frac(p * 0.1031f);
            p += math.dot(p, new float3(p.y + 33.33f, p.z + 33.33f, p.x + 33.33f));
            return math.frac((p.x + p.y) * p.z);
        }

        private static float ValueNoise(float3 x)
        {
            float3 i = math.floor(x);
            float3 f = math.frac(x);
            f = f * f * (3f - 2f * f);
            float nx00 = math.lerp(Hash13(i), Hash13(i + new float3(1f, 0f, 0f)), f.x);
            float nx10 = math.lerp(Hash13(i + new float3(0f, 1f, 0f)), Hash13(i + new float3(1f, 1f, 0f)), f.x);
            float nx01 = math.lerp(Hash13(i + new float3(0f, 0f, 1f)), Hash13(i + new float3(1f, 0f, 1f)), f.x);
            float nx11 = math.lerp(Hash13(i + new float3(0f, 1f, 1f)), Hash13(i + new float3(1f, 1f, 1f)), f.x);
            return math.lerp(math.lerp(nx00, nx10, f.y), math.lerp(nx01, nx11, f.y), f.z);
        }

        private static float Fbm(float3 p)
        {
            float sum = 0f;
            float amp = 0.5f;
            float3 q = p;
            for (int i = 0; i < 4; i++)
            {
                sum += ValueNoise(q) * amp;
                q *= 2.02f;
                amp *= 0.5f;
            }
            return sum;
        }

        /// <summary>
        /// Physically based procedural sky: single-scattering atmosphere plus a
        /// limb-darkened sun disc. Used by <c>Environment.SkyType.Procedural</c>.
        /// </summary>
        /// <param name="rayDir">View ray direction</param>
        /// <param name="lightDir">Direction toward the sun</param>
        /// <param name="sunColor">Sun radiance color</param>
        /// <param name="planetRadius">Planet radius in world units</param>
        /// <param name="atmosphereHeight">Atmosphere shell thickness</param>
        /// <param name="density">Overall atmosphere density multiplier</param>
        /// <param name="mieAnisotropy">Mie phase forward-scattering term</param>
        /// <param name="rayLength">Distance the ray travels through the atmosphere</param>
        /// <param name="sunAngularRadius">Sun disc angular radius in radians</param>
        /// <param name="sunFalloff">Soft edge width in radians</param>
        /// <param name="sunIntensity">Sun radiance multiplier</param>
        public static float3 ProceduralSky(
            float3 rayDir, float3 lightDir, float3 sunColor,
            float planetRadius, float atmosphereHeight, float density, float mieAnisotropy,
            float rayLength, float sunAngularRadius, float sunFalloff, float sunIntensity, float exposure,
            float cloudCoverage, float cloudDensity, float cloudBase, float cloudThickness, float cloudScale, float cloudSpeed, float time)
        {
            // Single scattering (Rayleigh + Mie) with a limb-darkened sun disc.
            // Single scattering (Rayleigh + Mie) with a limb-darkened sun disc.
            float R = math.max(planetRadius, 1f);
            float H = math.max(atmosphereHeight, 1f);
            float3 center = new float3(0f, -R, 0f);
            float3 origin = new float3(0f, 2f, 0f);
            float3 sun = sunColor * sunIntensity;
            float3 sunDir = math.normalize(lightDir + new float3(1e-5f, 1e-5f, 1e-5f));

            // Sun disc is added after the atmosphere so it is always visible.
            float disc = LimbDarkenedSun(rayDir, sunDir, sunAngularRadius, sunFalloff);
            float3 sunTint = math.lerp(sunColor, new float3(1f, 0.45f, 0.2f), math.pow(math.saturate(1f - sunDir.y), 6f));
            float3 discColor = sunTint * sunIntensity * disc * 25f;

            float2 tAtmos = SphereIntersection(origin, rayDir, center, R + H);
            float upFac = math.saturate(1f - math.exp(-rayDir.y * 7f));
            float3 baseSky = math.lerp(new float3(0.55f, 0.62f, 0.75f) * 0.22f, new float3(0.06f, 0.12f, 0.30f), upFac);

            if (tAtmos.y <= 0f)
            {
                // Below the horizon: continue the horizon haze so there is no hard edge.
                return (new float3(0.33f, 0.40f, 0.52f) + discColor) * exposure;
            }

            float tMax = tAtmos.y;
            float2 tPlanet = SphereIntersection(origin, rayDir, center, R);
            if (tPlanet.x > 0f)
            {
                // Ray hits the planet: distant ground, already in pre-exposure units.
                float groundToHaze = math.saturate(-rayDir.y * 3f);
                float3 ground = math.lerp(new float3(0.050f, 0.065f, 0.095f), new float3(0.020f, 0.026f, 0.022f), groundToHaze);
                return (ground + discColor) * exposure;
            }
            if (tPlanet.x > 0f) tMax = math.min(tMax, tPlanet.x);

            const float RAYLEIGH_SCALE = 8000f;
            const float MIE_SCALE = 1200f;
            const float PI = 3.14159265f;
            float3 rayleighCoeff = new float3(5.8e-6f, 13.5e-6f, 33.1e-6f);
            float mieCoeff = 21e-6f;
            const int VIEW_STEPS = 8;
            const int LIGHT_STEPS = 4;

            float segment = tMax / VIEW_STEPS;
            float3 rayleighAccum = new float3(0f);
            float3 mieAccum = new float3(0f);
            float depthR = 0f, depthM = 0f;

            for (int i = 0; i < VIEW_STEPS; i++)
            {
                float3 p = origin + rayDir * ((i + 0.5f) * segment);
                float height = math.max(math.length(p - center) - R, 0f);
                float hR = math.exp(-height / RAYLEIGH_SCALE) * segment;
                float hM = math.exp(-height / MIE_SCALE) * segment;
                depthR += hR;
                depthM += hM;

                float2 tLight = SphereIntersection(p, sunDir, center, R + H);
                float lightSeg = tLight.y / LIGHT_STEPS;
                float lightDepthR = 0f, lightDepthM = 0f;
                bool occluded = false;
                for (int j = 0; j < LIGHT_STEPS; j++)
                {
                    float3 lp = p + sunDir * ((j + 0.5f) * lightSeg);
                    float lh = math.max(math.length(lp - center) - R, 0f);
                    lightDepthR += math.exp(-lh / RAYLEIGH_SCALE) * lightSeg;
                    lightDepthM += math.exp(-lh / MIE_SCALE) * lightSeg;
                    if (math.length(lp - center) < R) occluded = true;
                }

                float mieDepth = 1.1f * (depthM + lightDepthM);
                float3 tau = rayleighCoeff * (depthR + lightDepthR)
                    + new float3(mieCoeff * mieDepth, mieCoeff * mieDepth, mieCoeff * mieDepth);
                float3 attn = occluded ? new float3(0f) : math.exp(-tau);

                rayleighAccum += hR * attn;
                mieAccum += hM * attn;
            }

            float mu = math.dot(rayDir, sunDir);
            float phaseR = 3f / (16f * PI) * (1f + mu * mu);
            float g = math.clamp(mieAnisotropy, -0.9f, 0.9f);
            float denom = math.pow(math.max(1f + g * g - 2f * g * mu, 1e-4f), 1.5f);
            float phaseM = 3f / (8f * PI) * ((1f - g * g) * (1f + mu * mu)) / ((2f + g * g) * denom);

            float3 color = sun * (rayleighAccum * rayleighCoeff * phaseR + mieAccum * mieCoeff * phaseM);
            color = color * 0.25f;

            float horizonMix = math.saturate(1f - math.abs(rayDir.y) * 10f);
            color = math.lerp(color, new float3(0.050f, 0.065f, 0.095f), horizonMix * 0.85f);

            // Volumetric clouds: 3D density field with height falloff (no slab edges)
            if (cloudCoverage > 0.001f && rayDir.y > 0.005f)
            {
                float tCloud = 60f;
                float tEnd = math.min(7.0e4f, 4.5e4f / math.max(rayDir.y, 0.05f));
                float3 wind = new float3(time * cloudSpeed * 30f, 0f, time * cloudSpeed * 12f);
                float transmittance = 1f;
                float3 accum = new float3(0f);
                float cover = cloudCoverage;

                for (int i = 0; i < 64; i++)
                {
                    if (tCloud > tEnd || transmittance < 0.04f) break;

                    float3 p = origin + rayDir * tCloud;
                    float3 q = (p + wind) * cloudScale;
                    float3 warp = new float3(Fbm(q * 0.6f), Fbm(q * 0.6f + new float3(5.2f, 1.3f, 8.1f)), Fbm(q * 0.6f + new float3(9.7f, 3.1f, 2.2f))) - new float3(0.5f, 0.5f, 0.5f);
                    float baseShape = Fbm(q + warp * 0.8f);
                    float billow = 1f - math.abs(Fbm(q * 2.3f + warp) * 2f - 1f);
                    float f = math.saturate(baseShape * 0.75f + billow * 0.35f);
                    float heightFalloff = math.saturate((p.y - cloudBase) / math.max(cloudThickness, 1f));
                    float d = math.saturate((f - (1f - cover) * 1.05f) * 2.5f) * math.saturate(heightFalloff * 8f) * math.saturate((1f - heightFalloff) * 4f) * cloudDensity;

                    if (d > 0.002f)
                    {
                        float fSun = Fbm((p + wind + sunDir * 120f) * cloudScale);
                        float dif = math.saturate((fSun - (1f - cover)) * 6f);

                        float shadowDepth = 0f;
                        for (int j = 1; j <= 3; j++)
                        {
                            float3 lp = p + sunDir * (j * 90f);
                            float lf = Fbm((lp + wind) * cloudScale);
                            shadowDepth += math.saturate((lf - (1f - cover)) * 6f);
                        }

                        float shadow = math.exp(-shadowDepth * 0.7f);
                        float ms = 0.6f * shadow + 0.3f * math.exp(-shadowDepth * 0.2f) + 0.1f;
                        float powder = 1f - math.exp(-d * 3f);
                        float glare = 0.8f + 1.4f * math.pow(math.saturate(math.dot(rayDir, sunDir)), 8f);
                        float ao = math.exp(-d * 0.8f);

                        float3 cloudLit = new float3(0.45f, 0.55f, 0.72f) * 0.05f * ao;
                        float muC = math.dot(rayDir, sunDir);
                        float g1 = 0.8f, g2 = -0.3f;
                        float p1 = (1f - g1 * g1) / math.pow(math.max(1f + g1 * g1 - 2f * g1 * muC, 1e-3f), 1.5f);
                        float p2 = (1f - g2 * g2) / math.pow(math.max(1f + g2 * g2 - 2f * g2 * muC, 1e-3f), 1.5f);
                        float phase = (0.7f * p1 + 0.3f * p2) / 12.566f;
                        cloudLit += sunColor * (0.30f + 0.70f * dif) * ms * powder * (1f + 3f * phase) * glare * 0.18f;

                        float alpha = math.saturate(d * 0.22f) * math.saturate(rayDir.y * 10f) * math.exp(-tCloud / 4.0e4f);
                        accum += cloudLit * alpha * transmittance;
                        transmittance *= (1f - alpha);
                    }

                    tCloud += math.max(tCloud * 0.10f, 30f);
                }

                color = color * transmittance + accum;
            }
            float sunCos = math.saturate(math.dot(rayDir, sunDir));
            color += sunTint * (0.020f * math.pow(sunCos, 300f) + 0.004f * math.pow(sunCos, 18f));
            color += discColor;
            return color * exposure * math.saturate(density);
        }

        /// <summary>
        /// Soft-edged sun disc with simple limb darkening (darker toward the edge).
        /// </summary>
        /// <param name="rayDir">View ray direction</param>
        /// <param name="lightDir">Direction toward the sun</param>
        /// <param name="angularRadius">Sun angular radius in radians</param>
        /// <param name="falloff">Soft edge width in radians</param>
        /// <returns>Disc intensity in the range [0, 1]</returns>
        public static float LimbDarkenedSun(float3 rayDir, float3 lightDir, float angularRadius, float falloff)
        {
            float cosAngle = math.clamp(math.dot(rayDir, lightDir), -1f, 1f);
            float angle = math.acos(cosAngle);
            float radius = math.max(angularRadius, 1e-4f);
            float soft = math.max(falloff, 1e-4f);
            if (angle > radius + soft) return 0f;

            float edge = math.saturate((radius + soft - angle) / soft);
            float normalized = math.saturate(1f - (angle * angle) / (radius * radius));
            float limb = 0.35f + 0.65f * math.sqrt(normalized);
            return edge * limb;
        }

        public static float3 GetSunDisc(float3 rayDir, float3 lightDir, float sunDiscSize)
        {
            float a = math.cos(0.00436f * sunDiscSize);
            float costh = math.dot(rayDir, lightDir);
            float disc = math.sqrt(math.smoothstep(a, 1f, costh));
            return new float3(disc, disc, disc);
        }
    }
}































