
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering.Denoising
{
    /// <summary>
    /// Unified reflection/refraction denoiser, one A-trous/bilateral pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The renderer splits the shaded frame into three linear radiance terms: the stable
    /// base shading, the stochastic first-surface reflection and the refracted light.
    /// This pass filters only the two secondary terms, so albedo, texture detail and
    /// direct lighting are never blurred.
    /// </para>
    /// <para>
    /// The filter merges both denoisers that used to run in sequence: the edge-aware
    /// A-trous wavelet cascade (5x5 B3-spline kernel, doubling step size) and the
    /// bilateral "Division" filter (Gaussian spatial falloff with depth, normal and
    /// material edge stops). Per-pixel data comes from the material G-buffer, so the
    /// roughness that drives the weights is the value sampled from the material's
    /// roughness texture rather than one value for the whole object.
    /// </para>
    /// <para>
    /// Reflection passes use a roughness-dependent number of wavelet scales and a
    /// luminance edge stop, which keeps mirror reflections sharp and averages the wide
    /// lobe of semi-rough surfaces. Refraction passes use the same edge stops but no
    /// luminance stop, so rough glass blurs its refracted background into frosted glass
    /// without tracing extra refraction rays.
    /// </para>
    /// </remarks>
    [Shader]
    public struct UnifiedDenoiseShader
    {
        /// <summary>Width of the image.</summary>
        public float width;

        /// <summary>Height of the image.</summary>
        public float height;

        /// <summary>Distance between filter taps in pixels (doubles each pass).</summary>
        public int stepSize;

        /// <summary>Zero-based filter pass, used to widen the roughness response.</summary>
        public int passIndex;

        /// <summary>0 = reflection pass, 1 = refraction (frost) pass.</summary>
        public int filterRefraction;

        /// <summary>Camera denoise strength (0 keeps the noisy image).</summary>
        public float strength;

        /// <summary>Reflection radiance to filter (linear).</summary>
        [ShaderResource] public Texture2D<float4> inputReflection;

        /// <summary>Refracted radiance to filter (linear).</summary>
        [ShaderResource] public Texture2D<float4> inputRefraction;

        /// <summary>Depth (r) and world normals (gba).</summary>
        [ShaderResource] public Texture2D<float4> depthNormals;

        /// <summary>Roughness, metallic, flags and reflection weight of the primary hit.</summary>
        [ShaderResource] public Texture2D<float4> materialBuffer;

        /// <summary>Per-pixel object index.</summary>
        [ShaderResource] public Buffer<uint2> objectIdBuffer;

        /// <summary>Separable B3-spline kernel weights [1 4 6 4 1] / 16.</summary>
        [ShaderResource] public Buffer<float> kernelBuffer;

        /// <summary>Filtered reflection target for this pass.</summary>
        [ShaderResource] public RWTexture2D<float4> outputReflection;

        /// <summary>Filtered refraction target for this pass.</summary>
        [ShaderResource] public RWTexture2D<float4> outputRefraction;

        private static float Luminance(float3 color)
        {
            return color.x * 0.2126f + color.y * 0.7152f + color.z * 0.0722f;
        }

        /// <summary>
        /// Filters one of the two secondary channels. Only the selected channel's output
        /// is written; each channel owns its own ping-pong textures, so a dispatch never
        /// writes two aliasing views of the same resource.
        /// </summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            if (pixel.x >= (int)width || pixel.y >= (int)height) return;

            float4 centerMaterial = materialBuffer[pixel];
            int flags = (int)centerMaterial.z;
            int centerObject = (int)objectIdBuffer[pixel.x + pixel.y * (int)width].x;
            float3 centerReflection = inputReflection[pixel].rgb;
            float3 centerRefraction = inputRefraction[pixel].rgb;

            // Pixels that carry no secondary radiance are passed through untouched: the
            // sky and purely diffuse materials therefore keep every bit of detail.
            bool hasSecondary = filterRefraction == 1 ? (flags & 2) != 0 : (flags & 1) != 0;
            if (centerObject < 0 || !hasSecondary)
            {
                if (filterRefraction == 1) outputRefraction[pixel] = inputRefraction[pixel];
                else outputReflection[pixel] = inputReflection[pixel];
                return;
            }

            float4 centerDN = depthNormals[pixel];
            float centerDepth = centerDN.x;
            float3 centerNormal = centerDN.yzw;
            float centerRoughness = centerMaterial.x;

            // How much of this pass applies at this roughness. Low roughness keeps the
            // sharp mirror image (only the tight first scales apply); as the lobe widens,
            // more wavelet scales blend in to average the stochastic samples. Rough glass
            // reaches the widest scales, which is what produces the frosted look.
            float passScale = (float)passIndex;
            float reflectionLow = 0.035f + 0.025f * passScale
                + 0.025f * passScale * math.max(passScale - 1f, 0f);
            float reflectionStrength = math.smoothstep(
                reflectionLow, reflectionLow + 0.055f + 0.02f * passScale, centerRoughness);
            float refractionLow = 0.01f + 0.05f * passScale + 0.03f * passScale * passScale;
            float refractionStrength = math.smoothstep(
                refractionLow, refractionLow + 0.04f + 0.07f * passScale, centerRoughness);
            float activeStrength = (filterRefraction == 1 ? refractionStrength : reflectionStrength) * strength;
            if (activeStrength <= 0.0001f)
            {
                if (filterRefraction == 1) outputRefraction[pixel] = inputRefraction[pixel];
                else outputReflection[pixel] = inputReflection[pixel];
                return;
            }

            // Edge stops. The normal exponent tightens as roughness drops so a mirror
            // reflection is never mixed across curvature, while rough lobes accept the
            // normal variation that belongs to their own scattering cone. Because this
            // channel holds reflection radiance only (not albedo or direct light), the
            // stop can be much more permissive than a full-colour bilateral filter.
            float normalExponent = math.lerp(48f, 4f, math.saturate(centerRoughness * 2.0f));
            float centerLuma = Luminance(centerReflection);
            float colorSigma = 0.15f + 1.40f * centerRoughness;

            float3 sum = new float3(0f);
            float sumWeight = 0f;

            // Fireflies need a colour-agnostic reference: comparing against the centre
            // pixel alone preserves an outlier (its normal neighbours are "different"),
            // so the first pass averages the *neighbours* with structural weights only
            // and stores the result in the alpha channel. Excluding the centre is what
            // lets a lone bright sample be clamped instead of protecting itself.
            float localLuma;
            float clampReference;
            if (filterRefraction == 0 && passIndex == 0)
            {
                localLuma = 0f;
                float localWeight = 0f;
                for (int yy = -2; yy <= 2; yy++)
                {
                    for (int xx = -2; xx <= 2; xx++)
                    {
                        if (xx == 0 && yy == 0) continue; // centre excluded on purpose
                        int2 samplePixel = pixel + new int2(xx * stepSize, yy * stepSize);
                        if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                            samplePixel.y < 0 || samplePixel.y >= (int)height)
                            continue;
                        if ((int)objectIdBuffer[samplePixel.x + samplePixel.y * (int)width].x != centerObject)
                            continue;

                        float4 sampleDN = depthNormals[samplePixel];
                        float depthDiff = math.abs(sampleDN.x - centerDepth) / math.max(centerDepth, 1e-5f);
                        float depthSigma = 0.03f + 0.15f * centerRoughness;
                        float depthWeight = math.exp(-(depthDiff * depthDiff) / (2f * depthSigma * depthSigma));
                        float normalDot = math.saturate(math.dot(centerNormal, sampleDN.yzw));
                        float normalWeight = math.pow(normalDot, normalExponent);
                        float sampleRoughness = materialBuffer[samplePixel].x;
                        float roughnessDiff = sampleRoughness - centerRoughness;
                        float roughnessWeight = math.exp(-(roughnessDiff * roughnessDiff) / (2f * 0.11f * 0.11f));
                        float structuralWeight = depthWeight * normalWeight * roughnessWeight;
                        localLuma += Luminance(inputReflection[samplePixel].rgb) * structuralWeight;
                        localWeight += structuralWeight;
                    }
                }
                localLuma = localLuma / math.max(localWeight, 1e-5f);

                // A second, much wider structural mean is the firefly reference. The
                // 5x5 mean is too local: a reflected sun disc can cover the whole window,
                // inflating the mean and letting the cluster through as a bright block.
                // Sampling a 5x5 grid at ±6 pixels sees beyond that cluster.
                clampReference = 0f;
                float wideWeight = 0f;
                for (int yy = -2; yy <= 2; yy++)
                {
                    for (int xx = -2; xx <= 2; xx++)
                    {
                        if (xx == 0 && yy == 0) continue;
                        int2 samplePixel = pixel + new int2(xx * 3 * stepSize, yy * 3 * stepSize);
                        if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                            samplePixel.y < 0 || samplePixel.y >= (int)height)
                            continue;
                        if ((int)objectIdBuffer[samplePixel.x + samplePixel.y * (int)width].x != centerObject)
                            continue;

                        float4 sampleDN = depthNormals[samplePixel];
                        float depthDiff = math.abs(sampleDN.x - centerDepth) / math.max(centerDepth, 1e-5f);
                        float depthSigma = 0.03f + 0.15f * centerRoughness;
                        float depthWeight = math.exp(-(depthDiff * depthDiff) / (2f * depthSigma * depthSigma));
                        float normalDot = math.saturate(math.dot(centerNormal, sampleDN.yzw));
                        float normalWeight = math.pow(normalDot, normalExponent);
                        float sampleRoughness = materialBuffer[samplePixel].x;
                        float roughnessDiff = sampleRoughness - centerRoughness;
                        float roughnessWeight = math.exp(-(roughnessDiff * roughnessDiff) / (2f * 0.11f * 0.11f));
                        float structuralWeight = depthWeight * normalWeight * roughnessWeight;
                        clampReference += Luminance(inputReflection[samplePixel].rgb) * structuralWeight;
                        wideWeight += structuralWeight;
                    }
                }
                clampReference = clampReference / math.max(wideWeight, 1e-5f);
            }
            else
            {
                localLuma = inputReflection[pixel].a;
                clampReference = localLuma;
            }

            // Mirror reflections are coherent: anchoring on the centre keeps genuine
            // high-contrast features (sun, bright sky) intact. From a few percent of
            // roughness upwards the neighbourhood mean is the only reliable outlier
            // reference, so a firefly cannot preserve itself by rejecting its normal
            // neighbours. The clamp always uses the neighbour mean, with a permissive
            // scale on near-mirror surfaces so resolved glints survive.
            float anchorLuma = math.lerp(centerLuma, localLuma,
                math.saturate((centerRoughness - 0.03f) * 20f));
            float clampScale = math.lerp(8f, 1.6f, math.saturate((centerRoughness - 0.03f) * 20f));
            float clampLimit = clampReference * clampScale + 0.02f;

            // A-trous 5x5 filter with adaptive step size, normalised by the full kernel
            // (the centre pixel is part of the weighted sum, not an extra unit weight).
            for (int yy = -2; yy <= 2; yy++)
            {
                for (int xx = -2; xx <= 2; xx++)
                {
                    int2 samplePixel = pixel + new int2(xx * stepSize, yy * stepSize);
                    if (samplePixel.x < 0 || samplePixel.x >= (int)width ||
                        samplePixel.y < 0 || samplePixel.y >= (int)height)
                        continue;

                    // Silhouettes and neighbouring objects must never mix.
                    if ((int)objectIdBuffer[samplePixel.x + samplePixel.y * (int)width].x != centerObject)
                        continue;

                    float4 sampleDN = depthNormals[samplePixel];
                    float depthDiff = math.abs(sampleDN.x - centerDepth) / math.max(centerDepth, 1e-5f);
                    float depthSigma = 0.03f + 0.15f * centerRoughness;
                    float depthWeight = math.exp(-(depthDiff * depthDiff) / (2f * depthSigma * depthSigma));
                    if (depthWeight < 0.005f) continue;

                    float normalDot = math.saturate(math.dot(centerNormal, sampleDN.yzw));
                    float normalWeight = math.pow(normalDot, normalExponent);
                    if (normalWeight < 0.0005f) continue;

                    // Texture-aware edge stop: a roughness map divides one surface into
                    // smooth and rough regions, and those regions have different noise
                    // statistics, so they must not be averaged together.
                    float sampleRoughness = materialBuffer[samplePixel].x;
                    float roughnessDiff = sampleRoughness - centerRoughness;
                    float roughnessWeight = math.exp(-(roughnessDiff * roughnessDiff) / (2f * 0.11f * 0.11f));

                    float spatialWeight = kernelBuffer[xx + 2] * kernelBuffer[yy + 2];
                    float weight = spatialWeight * depthWeight * normalWeight * roughnessWeight;
                    if (weight < 0.0001f) continue;

                    if (filterRefraction == 1)
                    {
                        // Frost is a blur of the refracted background: colour differences
                        // are expected and must not stop the filter.
                        sum += inputRefraction[samplePixel].rgb * weight;
                        sumWeight += weight;
                    }
                    else
                    {
                        float3 sampleReflection = inputReflection[samplePixel].rgb;

                        // Firefly clamp against the structural neighbourhood mean, so a
                        // lone bright sample is pulled into the local range instead of
                        // being excluded by the colour stop.
                        float sampleLuma = Luminance(sampleReflection);
                        if (sampleLuma > clampLimit)
                            sampleReflection *= clampLimit / math.max(sampleLuma, 1e-5f);

                        // Scale-invariant luminance edge stop against the anchor luma:
                        // keeps reflection edges and the mirror image sharp while
                        // averaging the noise of a wide lobe.
                        float ratio = math.log((Luminance(sampleReflection) + 0.004f) / (anchorLuma + 0.004f));
                        float colorWeight = math.exp(-(ratio * ratio) / (2f * colorSigma * colorSigma));

                        float reflectionWeight = weight * colorWeight;
                        sum += sampleReflection * reflectionWeight;
                        sumWeight += reflectionWeight;
                    }
                }
            }

            float3 filtered = sum / math.max(sumWeight, 1e-5f);
            if (filterRefraction == 1)
                outputRefraction[pixel] = new float4(math.lerp(centerRefraction, filtered, activeStrength), 1f);
            else
            {
                // Clamp the centre used for the blend as well: at low roughness the pass
                // only blends part of the filtered value, which would otherwise let a
                // surviving firefly keep its original brightness.
                float3 clampedCenter = centerReflection;
                if (centerLuma > clampLimit)
                    clampedCenter *= clampLimit / math.max(centerLuma, 1e-5f);
                outputReflection[pixel] = new float4(
                    math.lerp(clampedCenter, filtered, activeStrength), localLuma);
            }
        }
    }
}
