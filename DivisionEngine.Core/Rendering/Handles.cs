//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Rendering
{
    /// <summary>
    /// API for drawing editor handles and shapes in world space.
    /// </summary>
    public static class Handles
    {
        private static readonly Dictionary<uint, HandleShape> shapesToDraw = [];
        private static uint nextShapeId = 1000;

        public static void DrawBounds(uint entityId, float3 center, float3 size, float3 color, float thickness = 2.0f)
        {
            float3 halfSize = size * 0.5f;
            float3 min = center - halfSize;
            float3 max = center + halfSize;

            // Define the 12 edges of the bounding box
            var edges = new[]
            {
                // Bottom edges
                (new float3(min.x, min.y, min.z), new float3(max.x, min.y, min.z)),
                (new float3(max.x, min.y, min.z), new float3(max.x, min.y, max.z)),
                (new float3(max.x, min.y, max.z), new float3(min.x, min.y, max.z)),
                (new float3(min.x, min.y, max.z), new float3(min.x, min.y, min.z)),
                // Top edges
                (new float3(min.x, max.y, min.z), new float3(max.x, max.y, min.z)),
                (new float3(max.x, max.y, min.z), new float3(max.x, max.y, max.z)),
                (new float3(max.x, max.y, max.z), new float3(min.x, max.y, max.z)),
                (new float3(min.x, max.y, max.z), new float3(min.x, max.y, min.z)),
                // Vertical edges
                (new float3(min.x, min.y, min.z), new float3(min.x, max.y, min.z)),
                (new float3(max.x, min.y, min.z), new float3(max.x, max.y, min.z)),
                (new float3(max.x, min.y, max.z), new float3(max.x, max.y, max.z)),
                (new float3(min.x, min.y, max.z), new float3(min.x, max.y, max.z))
            };

            uint shapeId = nextShapeId++;
            foreach (var edge in edges)
            {
                shapesToDraw[shapeId] = new HandleShape
                {
                    Type = (uint)ShapeType.Line,
                    Start = edge.Item1,
                    End = edge.Item2,
                    Color = color,
                    Thickness = thickness,
                    EntityId = entityId,
                };
                shapeId++;
            }
        }

        public static void DrawLine(uint entityId, float3 start, float3 end, float3 color, float thickness = 2.0f)
        {
            uint shapeId = nextShapeId++;
            shapesToDraw[shapeId] = new HandleShape
            {
                Type = (uint)ShapeType.Line,
                Start = start,
                End = end,
                Color = color,
                Thickness = thickness,
                EntityId = entityId,
            };
        }

        public static void DrawWireCircle(uint entityId, float3 center, float radius, float3 color, float thickness = 2.0f)
        {
            uint shapeId = nextShapeId++;
            shapesToDraw[shapeId] = new HandleShape
            {
                Type = (uint)ShapeType.Circle,
                Center = center,
                Radius = radius,
                Color = color,
                Thickness = thickness,
                EntityId = entityId,
            };
        }

        public static void DrawWireSphere(uint entityId, float3 center, float radius, float3 color, float thickness = 2.0f)
        {
            // Draw 3 circles for sphere (XY, XZ, YZ planes)
            DrawWireCircle(entityId, center, radius, color, thickness);

            uint shapeId = nextShapeId++;
            shapesToDraw[shapeId] = new HandleShape
            {
                Type = (uint)ShapeType.CircleXZ,
                Center = center,
                Radius = radius,
                Color = color,
                Thickness = thickness,
                EntityId = entityId,
            };

            shapeId = nextShapeId++;
            shapesToDraw[shapeId] = new HandleShape
            {
                Type = (uint)ShapeType.CircleYZ,
                Center = center,
                Radius = radius,
                Color = color,
                Thickness = thickness,
                EntityId = entityId,
            };
        }

        public static void Clear()
        {
            shapesToDraw.Clear();
        }

        internal static Dictionary<uint, HandleShape> GetShapes() => shapesToDraw;
    }

    public struct HandleShape
    {
        public uint Type;
        public float3 Start;
        public float3 End;
        public float3 Center;
        public float Radius;
        public float3 Color;
        public float Thickness;
        public uint EntityId;
    }

    public enum ShapeType : uint
    {
        Line = 0,
        Circle = 1,
        CircleXZ = 2,
        CircleYZ = 3,
    }
}
