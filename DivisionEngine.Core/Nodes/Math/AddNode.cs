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
    /// Adds two float values.
    /// </summary>
    [NodeMenuItem("Math/Add", Description = "Adds two float values.", Icon = "Plus")]
    public sealed class AddNode : Node
    {
        /// <summary>Creates an add node.</summary>
        public AddNode()
        {
            Name = "Add";
            AddInput("A", 0f, min: -1000000, max: 1000000, step: 0.01);
            AddInput("B", 0f, min: -1000000, max: 1000000, step: 0.01);
            AddOutput<float>("Result");
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
            => context.GetInput<float>("A") + context.GetInput<float>("B");
    }

    /// <summary>
    /// Multiplies two float values.
    /// </summary>
    [NodeMenuItem("Math/Multiply", Description = "Multiplies two float values.", Icon = "Asterisk")]
    public sealed class MultiplyNode : Node
    {
        /// <summary>Creates a multiply node.</summary>
        public MultiplyNode()
        {
            Name = "Multiply";
            AddInput("A", 1f, min: -1000000, max: 1000000, step: 0.01);
            AddInput("B", 1f, min: -1000000, max: 1000000, step: 0.01);
            AddOutput<float>("Result");
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
            => context.GetInput<float>("A") * context.GetInput<float>("B");
    }

    /// <summary>
    /// Subtracts one float from another.
    /// </summary>
    [NodeMenuItem("Math/Subtract", Description = "Subtracts B from A.", Icon = "Minus")]
    public sealed class SubtractNode : Node
    {
        /// <summary>Creates a subtract node.</summary>
        public SubtractNode()
        {
            Name = "Subtract";
            AddInput("A", 0f, min: -1000000, max: 1000000, step: 0.01);
            AddInput("B", 0f, min: -1000000, max: 1000000, step: 0.01);
            AddOutput<float>("Result");
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
            => context.GetInput<float>("A") - context.GetInput<float>("B");
    }

    /// <summary>
    /// A sine wave generator, useful as the starting point for procedural audio.
    /// </summary>
    [NodeMenuItem("Audio/Sine Wave", Description = "Evaluates a sine wave at a given time.", Icon = "SineWave")]
    public sealed class SineWaveNode : Node
    {
        /// <summary>Frequency in hertz.</summary>
        [NodeProperty(DisplayName = "Frequency (Hz)", Min = 0.01, Max = 20000, Step = 1)]
        public float Frequency { get; set; } = 440f;

        /// <summary>Peak amplitude.</summary>
        [NodeProperty(Min = 0, Max = 1, Step = 0.01)]
        public float Amplitude { get; set; } = 1f;

        /// <summary>Creates a sine wave node.</summary>
        public SineWaveNode()
        {
            Name = "Sine Wave";
            AddInput("Time", 0f, min: 0, max: 100000, step: 0.001, tooltip: "Time in seconds.");
            AddOutput<float>("Sample");
        }

        /// <inheritdoc />
        public override object? Evaluate(NodeEvaluationContext context, NodePort? output)
        {
            float time = context.GetInput<float>("Time");
            return Amplitude * MathF.Sin(2f * MathF.PI * Frequency * time);
        }
    }
}
