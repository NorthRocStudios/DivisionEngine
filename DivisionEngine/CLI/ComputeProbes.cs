//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
// Probe shaders used by `DivisionEngine.Editor.exe --cli compute selftest`.
//
// They are deliberately small but exercise every DivisionCompute feature the
// post-processing effects need: RWTexture2D reads and writes, textures with
// multiple bindings, math intrinsics on DivisionMath vectors, helper functions,
// nested loops with branch/continue, and structured buffers with Length.
//
// Note the file-level type aliases: the translator emits a type by its C# name, so
// F2/F3/F4/I2/U3 resolve to the HLSL float2/float3/float4/int2/uint3 types without
// depending on the project-wide aliases in GlobalUsings.cs.
//
#pragma warning disable CA1416 // Validate platform compatibility

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using F2 = DivisionEngine.MathLib.float2;
using F3 = DivisionEngine.MathLib.float3;
using F4 = DivisionEngine.MathLib.float4;
using I2 = DivisionEngine.MathLib.int2;
using U3 = DivisionEngine.MathLib.uint3;

namespace DivisionEngine.Editor.Compute
{
    /// <summary>
    /// Copies every texel from <c>input</c> to <c>output</c>. Verifies that a
    /// <see cref="RWTexture2D{T}"/> can be read and written through the same binding.
    /// </summary>
    [Shader]
    public struct CopyProbe
    {
        /// <summary>Source texture.</summary>
        [ShaderResource] public RWTexture2D<F4> input;

        /// <summary>Destination texture.</summary>
        [ShaderResource] public RWTexture2D<F4> output;

        /// <summary>Copies one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            output[pixel] = input[pixel];
        }
    }

    /// <summary>
    /// Exercises the math intrinsics and vector operators the effects rely on.
    /// The CPU reference in the CLI uses the same DivisionMath calls, so this
    /// probe validates C# to HLSL translation fidelity.
    /// </summary>
    [Shader]
    public struct MathProbe
    {
        /// <summary>Result texture.</summary>
        [ShaderResource] public RWTexture2D<F4> output;

        private static float Luminance(F3 rgb)
        {
            return math.dot(rgb, new F3(0.2126f, 0.7152f, 0.0722f));
        }

        private static F3 Tint(F3 rgb, float amount)
        {
            F3 warm = new F3(1.0f, 0.6f, 0.25f);
            return math.lerp(rgb, rgb * warm, amount);
        }

        /// <summary>Writes one procedural texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            F2 uv = new F2((float)pixel.x / 256.0f, (float)pixel.y / 192.0f);
            F2 centered = uv - new F2(0.5f, 0.5f);
            float radial = math.length(centered);
            float wave = math.sin(uv.x * 12.0f) * math.cos(uv.y * 9.0f);
            float ramp = math.saturate(0.5f + 0.5f * wave);
            float glint = math.pow(ramp, 2.0f);
            float falloff = math.exp(-radial * 3.0f);
            float edge = math.smoothstep(0.2f, 0.8f, radial);
            float banding = math.floor(uv.y * 8.0f) / 8.0f;
            F3 baseColor = new F3(ramp, glint, falloff);
            baseColor = Tint(baseColor, 0.35f * edge);
            baseColor = baseColor + new F3(0.05f) * math.frac(uv.x * 7.0f);
            baseColor = baseColor + new F3(banding * 0.1f);
            float lum = Luminance(baseColor);
            F3 normal = math.normalize(new F3(centered.x, centered.y, 0.5f));
            F3 tangent = math.normalize(math.cross(normal, new F3(0.0f, 1.0f, 0.0f)));
            F3 bitangent = math.cross(normal, tangent);
            float wrap = math.max(math.min(lum, 0.9f), 0.05f);
            F4 color = new F4(baseColor * wrap + tangent * 0.1f + bitangent * 0.05f, 1.0f);
            output[pixel] = math.clamp(color, new F4(0.0f), new F4(1.0f));
        }
    }

    /// <summary>
    /// A 9-tap horizontal Gaussian blur with an early-out branch and a loop,
    /// mirroring the structure of the two-pass blur post-processing effect.
    /// </summary>
    [Shader]
    public struct BlurProbe
    {
        /// <summary>Source texture.</summary>
        [ShaderResource] public RWTexture2D<F4> input;

        /// <summary>Destination texture.</summary>
        [ShaderResource] public RWTexture2D<F4> output;

        private static float GaussianWeight(float x, float sigma)
        {
            float sigma2 = sigma * sigma;
            return 1.0f / math.sqrt(2.0f * 3.141592654f * sigma2) * math.exp(-(x * x) / (2.0f * sigma2));
        }

        /// <summary>Blurs one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            F4 result = new F4(0.0f);
            float totalWeight = 0.0f;

            for (int i = -4; i <= 4; i++)
            {
                I2 samplePixel = pixel + new I2(i, 0);
                if (samplePixel.x < 0 || samplePixel.x >= 256) continue;
                if (samplePixel.y < 0 || samplePixel.y >= 192) continue;

                float weight = GaussianWeight((float)i, 2.0f);
                result = result + input[samplePixel] * weight;
                totalWeight = totalWeight + weight;
            }

            F4 averaged = totalWeight > 0.0f ? result / totalWeight : input[pixel];
            output[pixel] = averaged;
        }
    }

    /// <summary>
    /// Element struct for <see cref="BufferProbe"/>.
    /// </summary>
    public struct ProbeItem
    {
        /// <summary>Scalar payload.</summary>
        public float value;

        /// <summary>Vector payload.</summary>
        public F3 color;

        /// <summary>Integer payload.</summary>
        public int index;
    }

    /// <summary>
    /// Reads a structured buffer, transforms each element and writes it back.
    /// Validates structured-buffer binding, element layout, and <c>Length</c>.
    /// </summary>
    [Shader]
    public struct BufferProbe
    {
        /// <summary>Input elements.</summary>
        [ShaderResource] public StructuredBuffer<ProbeItem> input;

        /// <summary>Output elements.</summary>
        [ShaderResource] public RWStructuredBuffer<ProbeItem> output;

        /// <summary>Transforms one element per thread.</summary>
        [Kernel(64, 1, 1)]
        public void Execute(U3 id)
        {
            uint index = id.x;
            if (index >= (uint)input.Length) return;

            ProbeItem item = input[index];
            item.value = item.value * 2.0f;
            item.color = item.color * 0.5f + new F3(0.25f, 0.0f, 0.0f);
            item.index = item.index + 1;
            output[index] = item;
        }
    }

    /// <summary>
    /// Reads effect parameters from the shader's uniform constant buffer and writes
    /// a gradient. Validates the Unity-style uniform model: plain fields on the
    /// shader struct, set by name from C#, and packed into <c>cbuffer DivisionParams</c>.
    /// The field order is deliberate: <c>float4</c> after three floats must be pushed
    /// to the next 16-byte register, and <c>float3</c> + <c>bool</c> must pack into
    /// one more register.
    /// </summary>
    [Shader]
    public struct UniformProbe
    {
        /// <summary>Texture width in texels.</summary>
        public float width;

        /// <summary>Texture height in texels.</summary>
        public float height;

        /// <summary>Gain applied to the horizontal ramp.</summary>
        public float gain;

        /// <summary>Four-component scale. Lands at byte 16 because of register packing.</summary>
        public F4 scale;

        /// <summary>Tint color.</summary>
        public F3 tint;

        /// <summary>True inverts the gradient.</summary>
        public bool invert;

        /// <summary>Result texture.</summary>
        [ShaderResource] public RWTexture2D<F4> output;

        /// <summary>Writes one gradient texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            F2 uv = new F2((float)pixel.x / width, (float)pixel.y / height);

            F4 scaled = new F4(uv.x, uv.y, gain, height) * scale;
            F3 tinted = tint * scaled.x + tint * scaled.y;
            float invertAmount = invert ? 1.0f : 0.0f;
            F3 result = tinted * (1.0f - invertAmount) + (new F3(1.0f) - tinted) * invertAmount;
            output[pixel] = new F4(result, 1.0f);
        }
    }
    /// <summary>
    /// Shared static helper class, standing in for the engine's <c>ShaderMath</c>,
    /// <c>PBR</c> and <c>SDFPrimitives</c> classes. Exercises on-demand helper
    /// translation, helper constants, and <c>out</c> parameters.
    /// </summary>
    public static class ProbeShaderMath
    {
        /// <summary>Helper constant (const field on a helper class).</summary>
        public const float Gain = 0.5f;

        /// <summary>Helper constant used in a vector expression.</summary>
        public const float Bias = 0.25f;

        /// <summary>Luminance helper.</summary>
        public static float Luminance(F3 rgb)
        {
            return math.dot(rgb, new F3(0.2126f, 0.7152f, 0.0722f));
        }

        /// <summary>Helper that calls another helper.</summary>
        public static F3 Tint(F3 rgb, float amount)
        {
            F3 warm = new F3(1.0f, 0.6f, 0.25f);
            return math.lerp(rgb, rgb * warm, amount);
        }

        /// <summary>Helper with out parameters.</summary>
        public static void Sample(F3 baseColor, out F3 result, out float weight)
        {
            result = baseColor * Gain + new F3(Bias);
            weight = Luminance(result);
        }
    }

    /// <summary>
    /// Exercises helper-class calls, helper constants, shader-struct constants,
    /// <c>out</c> parameters and <c>switch</c> in one shader.
    /// </summary>
    [Shader]
    public struct HelperProbe
    {
        /// <summary>Uniform combined with the helper results.</summary>
        public float gain;

        /// <summary>Result texture.</summary>
        [ShaderResource] public RWTexture2D<F4> output;

        private const float LocalScale = 2f;

        private static float Classify(float value)
        {
            switch ((int)(value * 4f))
            {
                case 0: return 0.1f;
                case 1: return 0.4f;
                case 2: return 0.7f;
                default: return 1f;
            }
        }

        /// <summary>Writes one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            F2 uv = new F2((float)pixel.x / 256.0f, (float)pixel.y / 192.0f);

            F3 color = new F3(uv.x, uv.y, math.frac(uv.x + uv.y));
            F3 tinted = ProbeShaderMath.Tint(color, 0.5f);

            F3 sampled = new F3(0f);
            float weight = 0f;
            ProbeShaderMath.Sample(tinted, out sampled, out weight);

            float scaled = Classify(uv.x) * LocalScale * weight * gain;
            output[pixel] = new F4(sampled * scaled, 1.0f);
        }
    }
    /// <summary>
    /// Reads a typed buffer and writes a typed buffer. Validates
    /// <c>Buffer&lt;T&gt;</c>/<c>RWBuffer&lt;T&gt;</c> translation and binding.
    /// </summary>
    [Shader]
    public struct TypedBufferProbe
    {
        /// <summary>Input elements.</summary>
        [ShaderResource] public Buffer<uint> input;

        /// <summary>Output elements.</summary>
        [ShaderResource] public RWBuffer<uint> output;

        /// <summary>Transforms one element per thread.</summary>
        [Kernel(64, 1, 1)]
        public void Execute(U3 id)
        {
            uint index = id.x;
            if (index >= (uint)input.Length) return;

            output[index] = input[index] * 2u + 1u;
        }
    }
}