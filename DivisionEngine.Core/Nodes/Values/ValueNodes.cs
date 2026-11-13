//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Nodes.Values
{
    /// <summary>
    /// Builds a vector3 from three float inputs.
    /// </summary>
    [NodeMenuItem("Input/Vector 3", Description = "Builds a vector3 from three floats.", Icon = "Axis")]
    public sealed class Vector3Node : Node
    {
        /// <summary>Creates a vector3 node.</summary>
        public Vector3Node()
        {
            Name = "Vector 3";
            AddInput("X", 0f, step: 0.01);
            AddInput("Y", 0f, step: 0.01);
            AddInput("Z", 0f, step: 0.01);
            AddOutput("Value", NodeDataType.Vector3);
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
            => new float3(
                context.GetInput<float>("X"),
                context.GetInput<float>("Y"),
                context.GetInput<float>("Z"));
    }

    /// <summary>
    /// Splits a vector3 into float components.
    /// </summary>
    [NodeMenuItem("Math/Split Vector 3", Description = "Splits a vector3 into components.", Icon = "CallSplit")]
    public sealed class SplitVector3Node : Node
    {
        /// <summary>Creates a vector split node.</summary>
        public SplitVector3Node()
        {
            Name = "Split Vector 3";
            AddInput("Vector", new float3(0f, 0f, 0f), tooltip: "Vector to split.");
            AddOutput<float>("X");
            AddOutput<float>("Y");
            AddOutput<float>("Z");
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
        {
            float3 value = context.GetInput<float3>("Vector");
            string name = output?.Name ?? "X";
            return name switch
            {
                "Y" => value.y,
                "Z" => value.z,
                _ => value.x,
            };
        }
    }

    /// <summary>
    /// Builds a colour from normalized RGBA inputs.
    /// </summary>
    [NodeMenuItem("Input/Color", Description = "Builds an RGBA colour from float inputs.", Icon = "Palette")]
    public sealed class ColorNode : Node
    {
        /// <summary>Creates a colour node.</summary>
        public ColorNode()
        {
            Name = "Color";
            AddInput("R", 1f, min: 0, max: 1, step: 0.01);
            AddInput("G", 0.55f, min: 0, max: 1, step: 0.01);
            AddInput("B", 0.2f, min: 0, max: 1, step: 0.01);
            AddInput("A", 1f, min: 0, max: 1, step: 0.01);
            AddOutput("Color", NodeDataType.Color);
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
            => new float4(
                context.GetInput<float>("R"),
                context.GetInput<float>("G"),
                context.GetInput<float>("B"),
                context.GetInput<float>("A"));
    }
}
