//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathLib;
using System.Globalization;

namespace DivisionEngine.Nodes.Sdf
{
    /// <summary>
    /// Generates a sphere signed distance field.
    /// </summary>
    [NodeMenuItem("SDF/Primitives/Sphere", Description = "A sphere signed distance field.", Icon = "Circle")]
    public sealed class SdfSphereNode : Node
    {
        /// <summary>Creates a sphere node.</summary>
        public SdfSphereNode()
        {
            Name = "Sphere";
            AddInput("Radius", 1f, min: 0.001, max: 10000, step: 0.05);
            AddInput("Center", NodeDataType.Vector3, new float3(0f, 0f, 0f));
            AddInput("Color", NodeDataType.Color, new float4(1f, 0.55f, 0.2f, 1f));
            AddOutput("SDF", NodeDataType.Sdf);
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
        {
            float radius = MathF.Max(context.GetInput<float>("Radius"), 0.0001f);
            float3 center = context.GetInput("Center") as float3? ?? new float3(0, 0, 0);
            float4 color = context.GetInput("Color") as float4? ?? new float4(1, 1, 1, 1);
            return new SdfShape(point => math.length(point - center) - radius, color,
                string.Format(CultureInfo.InvariantCulture, "Sphere r={0:0.##}", radius));
        }
    }

    /// <summary>
    /// Generates a rounded box signed distance field.
    /// </summary>
    [NodeMenuItem("SDF/Primitives/Box", Description = "A rounded box signed distance field.", Icon = "CubeOutline")]
    public sealed class SdfBoxNode : Node
    {
        /// <summary>Creates a box node.</summary>
        public SdfBoxNode()
        {
            Name = "Box";
            AddInput("Size", NodeDataType.Vector3, new float3(0.6f, 0.6f, 0.6f), min: 0.001, max: 10000, step: 0.05);
            AddInput("Rounding", 0.05f, min: 0, max: 1, step: 0.005);
            AddInput("Color", NodeDataType.Color, new float4(0.35f, 0.65f, 1f, 1f));
            AddOutput("SDF", NodeDataType.Sdf);
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
        {
            float3 size = context.GetInput("Size") as float3? ?? new float3(0.6f);
            float rounding = MathF.Max(context.GetInput<float>("Rounding"), 0f);
            float4 color = context.GetInput("Color") as float4? ?? new float4(0.35f, 0.65f, 1f, 1f);
            float3 half = new(MathF.Max(size.x, 0.0001f), MathF.Max(size.y, 0.0001f), MathF.Max(size.z, 0.0001f));
            return new SdfShape(
                point =>
                {
                    float3 q = new(
                        MathF.Abs(point.x) - half.x,
                        MathF.Abs(point.y) - half.y,
                        MathF.Abs(point.z) - half.z);
                    float outside = MathF.Sqrt(
                        MathF.Max(q.x, 0f) * MathF.Max(q.x, 0f) +
                        MathF.Max(q.y, 0f) * MathF.Max(q.y, 0f) +
                        MathF.Max(q.z, 0f) * MathF.Max(q.z, 0f));
                    float inside = MathF.Min(MathF.Max(q.x, q.y), q.z);
                    return outside + inside - rounding;
                },
                color,
                string.Format(CultureInfo.InvariantCulture, "Box {0:0.##}x{1:0.##}x{2:0.##}",
                    size.x, size.y, size.z));
        }
    }

    /// <summary>
    /// Combines two signed distance fields with an optional smooth union.
    /// </summary>
    [NodeMenuItem("SDF/Operators/Union", Description = "Combines two SDF shapes (optionally smooth).", Icon = "Merge")]
    public sealed class SdfUnionNode : Node
    {
        /// <summary>Creates a union node.</summary>
        public SdfUnionNode()
        {
            Name = "Union";
            AddInput("A", NodeDataType.Sdf);
            AddInput("B", NodeDataType.Sdf);
            AddInput("Smoothness", 0.1f, min: 0, max: 10, step: 0.01);
            AddOutput("SDF", NodeDataType.Sdf);
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
        {
            SdfShape? a = context.GetInput("A") as SdfShape;
            SdfShape? b = context.GetInput("B") as SdfShape;
            float smoothness = MathF.Max(context.GetInput<float>("Smoothness"), 0f);

            if (a is null && b is null)
                return new SdfShape(_ => float.MaxValue, new float4(1, 1, 1, 1), "Union (empty)");
            if (a is null) return b!;
            if (b is null) return a;

            float4 color = a.Color;
            return new SdfShape(
                point =>
                {
                    float distanceA = a.Evaluate(point);
                    float distanceB = b.Evaluate(point);
                    if (smoothness <= 0f) return MathF.Min(distanceA, distanceB);

                    float h = MathF.Max(smoothness - MathF.Abs(distanceA - distanceB), 0f) / smoothness;
                    return MathF.Min(distanceA, distanceB) - h * h * smoothness * 0.25f;
                },
                color,
                string.Format(CultureInfo.InvariantCulture, "Union({0}, {1}, k={2:0.##})",
                    a.Description, b.Description, smoothness));
        }
    }

    /// <summary>
    /// A terminal node that previews the signed distance field feeding it.
    /// </summary>
    [NodeMenuItem("Output/SDF Preview", Description = "Final SDF output with a live preview.", Icon = "Monitor")]
    public sealed class SdfPreviewNode : Node
    {
        /// <summary>Creates an SDF preview node.</summary>
        public SdfPreviewNode()
        {
            Name = "SDF Preview";
            AddInput("SDF", NodeDataType.Sdf);
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
            => context.GetInput("SDF");
    }
}
