//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.MathLib;

namespace DivisionEngine.Rendering
{
    [Shader]
    public struct CustomShapeShader
    {
        /// <summary>Width of the target.</summary>
        public float width;

        /// <summary>Height of the target.</summary>
        public float height;

        /// <summary>Aspect ratio of the target.</summary>
        public float aspect;

        /// <summary>Camera screen distance.</summary>
        public float camScreenDist;

        /// <summary>Camera origin.</summary>
        public float3 cameraOrigin;

        /// <summary>Camera forward vector.</summary>
        public float3 camForward;

        /// <summary>Camera right vector.</summary>
        public float3 camRight;

        /// <summary>Camera up vector.</summary>
        public float3 camUp;

        /// <summary>Shape to draw. A single-element structured buffer because a struct uniform is not expressible in the packed b0 cbuffer.</summary>
        [ShaderResource] public StructuredBuffer<HandleShape> shapeData;

        /// <summary>Shape ID written where the shape covers a pixel.</summary>
        public uint shapeId;

        /// <summary>Render target.</summary>
        [ShaderResource] public RWTexture2D<float4> renderTexture;

        /// <summary>Per-pixel shape ID buffer.</summary>
        [ShaderResource] public RWBuffer<uint> shapeIdBuffer;

        private float3 WorldToScreen(float3 worldPos)
        {
            float3 relativePos = worldPos - cameraOrigin;
            float camX = math.dot(relativePos, camRight);
            float camY = math.dot(relativePos, camUp);
            float camZ = math.dot(relativePos, camForward);

            if (camZ <= 0.001f) return new float3(-1, -1, -1);

            float2 uv = new float2(camX / camZ / camScreenDist, camY / camZ / camScreenDist);
            uv.x = uv.x / aspect;

            float screenX = (uv.x * 0.5f + 0.5f) * width;
            float screenY = (uv.y * 0.5f + 0.5f) * height;
            return new float3(screenX, screenY, camZ);
        }

        private float LineSDF(float2 p, float2 a, float2 b, float thickness)
        {
            float2 pa = p - a;
            float2 ba = b - a;
            float h = math.saturate(math.dot(pa, ba) / math.dot(ba, ba));
            float d = math.length(pa - ba * h);
            return d - thickness;
        }

        private float CircleSDF(float2 p, float2 center, float radius, float thickness)
        {
            float d = math.length(p - center) - radius;
            if (d < 0) return float.MaxValue;
            return d - thickness;
        }

        private float3 DrawShape(float distance, float thickness, float3 color, float3 currentColor)
        {
            if (distance > thickness) return currentColor;
            float alpha = math.saturate(1.0f - distance / thickness);
            return math.lerp(currentColor, color, alpha);
        }

        /// <summary>Draws one shape over one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            // Trailing threads of the last 8x8 group fall outside the viewport; without this
            // guard their shape-ID writes land on the next scanline of the ID buffer.
            if (pixel.x >= width || pixel.y >= height) return;
            float2 uv = new float2(pixel.x, pixel.y);

            HandleShape shape = shapeData[0];
            float4 existingColor = renderTexture[pixel];
            float3 finalColor = existingColor.xyz;
            float distance = float.MaxValue;

            if (shape.Type == 0)
            {
                float3 startScreen = WorldToScreen(shape.Start);
                float3 endScreen = WorldToScreen(shape.End);
                if (startScreen.z > 0 && endScreen.z > 0)
                {
                    distance = LineSDF(uv, startScreen.xy, endScreen.xy, shape.Thickness);
                }
            }
            else if (shape.Type == 1 || shape.Type == 2 || shape.Type == 3)
            {
                float3 centerScreen = WorldToScreen(shape.Center);
                if (centerScreen.z > 0)
                {
                    // For spheres, we need to handle the projection properly
                    // For now, just draw a circle
                    float screenRadius = shape.Radius / centerScreen.z * camScreenDist;
                    distance = CircleSDF(uv, centerScreen.xy, screenRadius * 50, shape.Thickness);
                }
            }

            if (distance <= shape.Thickness)
            {
                shapeIdBuffer[pixel.x + pixel.y * (int)width] = shapeId;
                finalColor = DrawShape(distance, shape.Thickness, shape.Color, finalColor);
                renderTexture[pixel] = new float4(finalColor, 1.0f);
            }
        }
    }
}