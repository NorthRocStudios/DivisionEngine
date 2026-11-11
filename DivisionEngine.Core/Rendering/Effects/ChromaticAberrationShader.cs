
//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.

using DivisionEngine.Graphics;
using DivisionEngine.MathLib;
using F2 = DivisionEngine.MathLib.float2;
using F4 = DivisionEngine.MathLib.float4;
using I2 = DivisionEngine.MathLib.int2;
using U3 = DivisionEngine.MathLib.uint3;

namespace DivisionEngine.Rendering.Effects
{
    /// <summary>
    /// Very basic implementation of chromatic aberration (has some artifacts for now).
    /// </summary>
    [Shader]
    public struct ChromaticAberrationShader
    {
        /// <summary>Width of the input texture.</summary>
        public float width;

        /// <summary>Height of the input texture.</summary>
        public float height;

        /// <summary>Intensity of the chromatic aberration effect.</summary>
        public float intensity;

        /// <summary>Input render texture.</summary>
        [ShaderResource] public RWTexture2D<F4> inputTexture;

        /// <summary>Output texture with the aberration applied.</summary>
        [ShaderResource] public RWTexture2D<F4> outputTexture;

        /// <summary>Applies chromatic aberration to one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(U3 id)
        {
            I2 pixel = new I2((int)id.x, (int)id.y);
            if (pixel.x >= width || pixel.y >= height) return;

            F2 uv = new F2(pixel.x / width, pixel.y / height);
            F2 center = new F2(0.5f, 0.5f);
            float dist = math.distance(center, uv);

            F2 rUV = uv - new F2(-0.5f, 0.5f) * dist * intensity;
            F2 gUV = uv - new F2(0.7f, -0.6f) * dist * intensity;
            F2 bUV = uv - new F2(-0.3f, 0.4f) * dist * intensity;

            // Clamp to the image: the shifted UVs leave [0,1) at the screen border, and an
            // out-of-range texel read returns 0, which drew a dark fringe around the frame.
            int2 rPix = new int2(
                (int)(math.saturate(rUV.x) * (width - 1)),
                (int)(math.saturate(rUV.y) * (height - 1)));
            int2 gPix = new int2(
                (int)(math.saturate(gUV.x) * (width - 1)),
                (int)(math.saturate(gUV.y) * (height - 1)));
            int2 bPix = new int2(
                (int)(math.saturate(bUV.x) * (width - 1)),
                (int)(math.saturate(bUV.y) * (height - 1)));

            float r = inputTexture[rPix].x;
            float g = inputTexture[gPix].y;
            float b = inputTexture[bPix].z;

            outputTexture[pixel] = new F4(r, g, b, 1f);
        }
    }
}
