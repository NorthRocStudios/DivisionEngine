//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Nodes.Math
{
    /// <summary>
    /// A constant float value.
    /// </summary>
    [NodeMenuItem("Input/Float", Description = "A constant float value.", Icon = "Numeric")]
    public sealed class FloatNode : Node
    {
        /// <summary>Value produced by the node.</summary>
        [NodeProperty(DisplayName = "Value", Step = 0.1)]
        public float Value { get; set; } = 1f;

        /// <summary>Creates a float constant node.</summary>
        public FloatNode()
        {
            Name = "Float";
            AddOutput<float>("Value", "The constant value.");
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output) => Value;
    }
}
