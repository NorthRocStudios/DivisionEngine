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
    /// <summary>
    /// Renders editor transformation handles as screen-space overlays on SDF-rendered scenes.
    /// Draws colored axis lines and a center circle without raymarching.
    /// </summary>
    [Shader]
    public struct EditorHandleShader
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

        /// <summary>World position of the handles.</summary>
        public float3 handlePosition;

        /// <summary>Screen-space scale multiplier for the handles.</summary>
        public float handleScale;

        /// <summary>ID of the currently hovered handle (0 = none).</summary>
        public uint hoveredHandle;

        /// <summary>Render target.</summary>
        [ShaderResource] public RWTexture2D<float4> renderTexture;

        /// <summary>Per-pixel handle ID buffer.</summary>
        [ShaderResource] public RWBuffer<uint> handleIdBuffer;

        private const int ROTATION_RING_SEGMENTS = 48;

        /// <summary>
        /// Projects a 3D world position to 2D screen pixel coordinates.
        /// </summary>
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

        /// <summary>
        /// Returns the signed distance to a line segment.
        /// </summary>
        private static float LineSegmentSDF(float2 p, float2 a, float2 b)
        {
            float2 pa = p - a;
            float2 ba = b - a;
            float h = math.saturate(math.dot(pa, ba) / math.dot(ba, ba));
            return math.length(pa - ba * h);
        }

        /// <summary>
        /// Returns the signed distance to a circle.
        /// </summary>
        private static float CircleOutlineSDF(float2 p, float2 center, float radius, float thickness)
        {
            float d = math.length(p - center) - radius;
            if (d < 0) return float.MaxValue;
            return d - thickness;
        }

        /// <summary>
        /// Returns the signed distance to a square (axis-aligned rectangle).
        /// </summary>
        private static float SquareSDF(float2 p, float2 center, float size, float thickness)
        {
            float2 halfSize = new float2(size * 0.5f, size * 0.5f);
            float2 d = math.abs(p - center) - halfSize;
            float distToSquare = math.max(d.x, d.y);
            if (distToSquare < 0) return float.MaxValue;
            return distToSquare - thickness;
        }

        /// <summary>
        /// Returns the final color of a handle SDF.
        /// </summary>
        private static float3 DrawSDF(float distance, float thickness, float3 color, float3 currentColor, bool isHovered)
        {
            float alpha = math.saturate(1.0f - math.abs(distance) / thickness);
            if (isHovered)
            {
                alpha = math.saturate(alpha * 1.2f);
                color *= 1.5f;
            }
            return math.lerp(currentColor, color, alpha);
        }

        private float RotationRingDistance(float2 pixelUV, float3 center, float3 planeU, float3 planeV, float radius)
        {
            float minDist = float.MaxValue;
            float3 prevWorld = center + planeU * radius;
            float3 prevScreen = WorldToScreen(prevWorld);
            for (int i = 1; i <= ROTATION_RING_SEGMENTS; i++)
            {
                float angle = i / (float)ROTATION_RING_SEGMENTS * 6.28318530718f;
                float3 dir = math.cos(angle) * planeU + math.sin(angle) * planeV;
                float3 curWorld = center + dir * radius;
                float3 curScreen = WorldToScreen(curWorld);

                float d = LineSegmentSDF(pixelUV, prevScreen.xy, curScreen.xy);
                if (d < minDist) minDist = d;
                prevScreen = curScreen;
            }
            return minDist;
        }

        /// <summary>Draws the handles over one texel per thread.</summary>
        [Kernel(8, 8, 1)]
        public void Execute(uint3 id)
        {
            int2 pixel = new int2((int)id.x, (int)id.y);
            // Trailing threads of the last 8x8 group fall outside the viewport; without this
            // guard their handle-ID writes land on the next scanline of the ID buffer.
            if (pixel.x >= width || pixel.y >= height) return;
            float2 uv = new float2(pixel.x, pixel.y);
            float cameraDist = math.distance(cameraOrigin, handlePosition);

            float axisLength = handleScale * cameraDist;
            float scaleHandlePosition = axisLength * 0.6f; // Position of scale square (60% along the axis)

            // Calculate end points in world space
            float3 xEnd = handlePosition + new float3(axisLength, 0, 0);
            float3 yEnd = handlePosition + new float3(0, axisLength, 0);
            float3 zEnd = handlePosition + new float3(0, 0, axisLength);

            // Scale handle positions in world space
            float3 xScale = handlePosition + new float3(scaleHandlePosition, 0, 0);
            float3 yScale = handlePosition + new float3(0, scaleHandlePosition, 0);
            float3 zScale = handlePosition + new float3(0, 0, scaleHandlePosition);

            // Project main points to screen
            float3 centerScreen = WorldToScreen(handlePosition);
            float3 xEndScreen = WorldToScreen(xEnd);
            float3 yEndScreen = WorldToScreen(yEnd);
            float3 zEndScreen = WorldToScreen(zEnd);

            // Project scale handle positions to screen
            float3 xScaleScreen = WorldToScreen(xScale);
            float3 yScaleScreen = WorldToScreen(yScale);
            float3 zScaleScreen = WorldToScreen(zScale);

            if (centerScreen.z < 0) return;

            // Calculate screen-space arrow directions
            float2 xDir = math.normalize(xEndScreen.xy - centerScreen.xy);
            float2 yDir = math.normalize(yEndScreen.xy - centerScreen.xy);
            float2 zDir = math.normalize(zEndScreen.xy - centerScreen.xy);

            // Perpendicular directions for arrow wings
            float2 xPerp = new float2(-xDir.y, xDir.x);
            float2 yPerp = new float2(-yDir.y, yDir.x);
            float2 zPerp = new float2(-zDir.y, zDir.x);

            // Arrow wing endpoints
            float arrowSize = 12f;
            float2 xArrow1 = xEndScreen.xy - xDir * arrowSize * 0.8f + xPerp * arrowSize * 0.5f;
            float2 xArrow2 = xEndScreen.xy - xDir * arrowSize * 0.8f - xPerp * arrowSize * 0.5f;
            float2 yArrow1 = yEndScreen.xy - yDir * arrowSize * 0.8f + yPerp * arrowSize * 0.5f;
            float2 yArrow2 = yEndScreen.xy - yDir * arrowSize * 0.8f - yPerp * arrowSize * 0.5f;
            float2 zArrow1 = zEndScreen.xy - zDir * arrowSize * 0.8f + zPerp * arrowSize * 0.5f;
            float2 zArrow2 = zEndScreen.xy - zDir * arrowSize * 0.8f - zPerp * arrowSize * 0.5f;

            float lineThickness = 1.5f;
            float hoveredLineThickness = 3f;
            float circleRadius = 2f;
            float circleThickness = 1.5f;
            float squareSize = 4f; // Screen-space pixel size for scale squares

            // Use thicker lines when hovered
            float xThickness = (hoveredHandle == 1) ? hoveredLineThickness : lineThickness;
            float yThickness = (hoveredHandle == 2) ? hoveredLineThickness : lineThickness;
            float zThickness = (hoveredHandle == 3) ? hoveredLineThickness : lineThickness;
            float xScaleThickness = (hoveredHandle == 5) ? hoveredLineThickness : lineThickness;
            float yScaleThickness = (hoveredHandle == 6) ? hoveredLineThickness : lineThickness;
            float zScaleThickness = (hoveredHandle == 7) ? hoveredLineThickness : lineThickness;
            float circleThicknessActual = (hoveredHandle == 4) ? hoveredLineThickness : circleThickness;

            // Main axis lines
            float distX = (xEndScreen.z > 0) ? LineSegmentSDF(uv, centerScreen.xy, xEndScreen.xy) : float.MaxValue;
            float distY = (yEndScreen.z > 0) ? LineSegmentSDF(uv, centerScreen.xy, yEndScreen.xy) : float.MaxValue;
            float distZ = (zEndScreen.z > 0) ? LineSegmentSDF(uv, centerScreen.xy, zEndScreen.xy) : float.MaxValue;

            // Arrow lines
            float distXArrow1 = (xEndScreen.z > 0) ? LineSegmentSDF(uv, xEndScreen.xy, xArrow1) : float.MaxValue;
            float distXArrow2 = (xEndScreen.z > 0) ? LineSegmentSDF(uv, xEndScreen.xy, xArrow2) : float.MaxValue;
            float distYArrow1 = (yEndScreen.z > 0) ? LineSegmentSDF(uv, yEndScreen.xy, yArrow1) : float.MaxValue;
            float distYArrow2 = (yEndScreen.z > 0) ? LineSegmentSDF(uv, yEndScreen.xy, yArrow2) : float.MaxValue;
            float distZArrow1 = (zEndScreen.z > 0) ? LineSegmentSDF(uv, zEndScreen.xy, zArrow1) : float.MaxValue;
            float distZArrow2 = (zEndScreen.z > 0) ? LineSegmentSDF(uv, zEndScreen.xy, zArrow2) : float.MaxValue;

            // Scale squares
            float distXScale = (xScaleScreen.z > 0) ? SquareSDF(uv, xScaleScreen.xy, squareSize, xScaleThickness) : float.MaxValue;
            float distYScale = (yScaleScreen.z > 0) ? SquareSDF(uv, yScaleScreen.xy, squareSize, yScaleThickness) : float.MaxValue;
            float distZScale = (zScaleScreen.z > 0) ? SquareSDF(uv, zScaleScreen.xy, squareSize, zScaleThickness) : float.MaxValue;

            // Rotation
            float rotRadius = axisLength * 1.333f;

            float3 xRingU = new float3(0, 1, 0), xRingV = new float3(0, 0, 1);
            float3 yRingU = new float3(0, 0, 1), yRingV = new float3(1, 0, 0);
            float3 zRingU = new float3(1, 0, 0), zRingV = new float3(0, 1, 0);

            float xRotThickness = (hoveredHandle == 8) ? hoveredLineThickness : lineThickness;
            float yRotThickness = (hoveredHandle == 9) ? hoveredLineThickness : lineThickness;
            float zRotThickness = (hoveredHandle == 10) ? hoveredLineThickness : lineThickness;

            float distXRing = RotationRingDistance(uv, handlePosition, xRingU, xRingV, rotRadius);
            float distYRing = RotationRingDistance(uv, handlePosition, yRingU, yRingV, rotRadius);
            float distZRing = RotationRingDistance(uv, handlePosition, zRingU, zRingV, rotRadius);

            float distCenterCircle = CircleOutlineSDF(uv, centerScreen.xy, circleRadius, circleThicknessActual);

            float4 existingColor = renderTexture[pixel];
            float3 finalColor = existingColor.xyz;
            uint handleId = 0;

            // Check center circle first
            if (math.abs(distCenterCircle) <= circleThicknessActual)
            {
                handleId = 4;
                bool isHovered = hoveredHandle == 4;
                finalColor = DrawSDF(distCenterCircle, circleThicknessActual, new float3(1f, 1f, 1f), finalColor, isHovered);
            }
            // X Scale Square
            else if (math.abs(distXScale) <= xScaleThickness)
            {
                handleId = 5;
                bool isHovered = hoveredHandle == 5;
                finalColor = DrawSDF(distXScale, xScaleThickness, new float3(1f, 0.2f, 0.2f), finalColor, isHovered);
            }
            // Y Scale Square
            else if (math.abs(distYScale) <= yScaleThickness)
            {
                handleId = 6;
                bool isHovered = hoveredHandle == 6;
                finalColor = DrawSDF(distYScale, yScaleThickness, new float3(0.2f, 1f, 0.2f), finalColor, isHovered);
            }
            // Z Scale Square
            else if (math.abs(distZScale) <= zScaleThickness)
            {
                handleId = 7;
                bool isHovered = hoveredHandle == 7;
                finalColor = DrawSDF(distZScale, zScaleThickness, new float3(0.2f, 0.6f, 1f), finalColor, isHovered);
            }
            // X Rotation Ring
            else if (distXRing <= xRotThickness)
            {
                handleId = 8;
                bool isHovered = hoveredHandle == 8;
                finalColor = DrawSDF(distXRing, xRotThickness, new float3(1f, 0.2f, 0.2f), finalColor, isHovered);
            }
            // Y Rotation Ring
            else if (distYRing <= yRotThickness)
            {
                handleId = 9;
                bool isHovered = hoveredHandle == 9;
                finalColor = DrawSDF(distYRing, yRotThickness, new float3(0.2f, 1f, 0.2f), finalColor, isHovered);
            }
            // Z Rotation Ring
            else if (distZRing <= zRotThickness)
            {
                handleId = 10;
                bool isHovered = hoveredHandle == 10;
                finalColor = DrawSDF(distZRing, zRotThickness, new float3(0.2f, 0.4f, 1f), finalColor, isHovered);
            }
            // X axis
            else if (distX <= xThickness || distXArrow1 <= xThickness || distXArrow2 <= xThickness)
            {
                handleId = 1;
                bool isHovered = hoveredHandle == 1;
                if (distX <= xThickness)
                    finalColor = DrawSDF(distX, xThickness, new float3(1f, 0.2f, 0.2f), finalColor, isHovered);
                if (distXArrow1 <= xThickness)
                    finalColor = DrawSDF(distXArrow1, xThickness, new float3(1f, 0.2f, 0.2f), finalColor, isHovered);
                if (distXArrow2 <= xThickness)
                    finalColor = DrawSDF(distXArrow2, xThickness, new float3(1f, 0.2f, 0.2f), finalColor, isHovered);
            }
            // Y axis
            else if (distY <= yThickness || distYArrow1 <= yThickness || distYArrow2 <= yThickness)
            {
                handleId = 2;
                bool isHovered = hoveredHandle == 2;
                if (distY <= yThickness)
                    finalColor = DrawSDF(distY, yThickness, new float3(0.2f, 1f, 0.2f), finalColor, isHovered);
                if (distYArrow1 <= yThickness)
                    finalColor = DrawSDF(distYArrow1, yThickness, new float3(0.2f, 1f, 0.2f), finalColor, isHovered);
                if (distYArrow2 <= yThickness)
                    finalColor = DrawSDF(distYArrow2, yThickness, new float3(0.2f, 1f, 0.2f), finalColor, isHovered);
            }
            // Z axis
            else if (distZ <= zThickness || distZArrow1 <= zThickness || distZArrow2 <= zThickness)
            {
                handleId = 3;
                bool isHovered = hoveredHandle == 3;
                if (distZ <= zThickness)
                    finalColor = DrawSDF(distZ, zThickness, new float3(0.2f, 0.4f, 1f), finalColor, isHovered);
                if (distZArrow1 <= zThickness)
                    finalColor = DrawSDF(distZArrow1, zThickness, new float3(0.2f, 0.4f, 1f), finalColor, isHovered);
                if (distZArrow2 <= zThickness)
                    finalColor = DrawSDF(distZArrow2, zThickness, new float3(0.2f, 0.4f, 1f), finalColor, isHovered);
            }

            handleIdBuffer[pixel.x + pixel.y * (int)width] = handleId;
            renderTexture[pixel] = new float4(finalColor, existingColor.w);
        }
    }
}