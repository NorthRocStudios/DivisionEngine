//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Nodes
{
    /// <summary>
    /// Direction a port faces on a node.
    /// </summary>
    public enum NodePortDirection
    {
        /// <summary>The port receives values from upstream nodes.</summary>
        Input,
        /// <summary>The port produces values for downstream nodes.</summary>
        Output,
    }

    /// <summary>
    /// A single input or output socket on a <see cref="Node"/>. Input ports carry a
    /// default value that is used when nothing is connected, which lets a node be
    /// configured directly from the graph without extra inspector plumbing.
    /// </summary>
    public sealed class NodePort
    {
        internal NodePort(
            Node node,
            string id,
            string name,
            NodeDataType dataType,
            NodePortDirection direction,
            object? defaultValue,
            bool allowMultipleConnections,
            double? min,
            double? max,
            double step,
            string? tooltip)
        {
            Node = node;
            Id = id;
            Name = name;
            DataType = dataType;
            Direction = direction;
            DefaultValue = defaultValue;
            AllowMultipleConnections = allowMultipleConnections;
            Min = min;
            Max = max;
            Step = step;
            Tooltip = tooltip;
        }

        /// <summary>Stable identifier of this port within its node (for example <c>in.Radius</c>).</summary>
        public string Id { get; }

        /// <summary>Human readable port name.</summary>
        public string Name { get; }

        /// <summary>Value kind and optional CLR type carried by the port.</summary>
        public NodeDataType DataType { get; }

        /// <summary>Direction the port faces.</summary>
        public NodePortDirection Direction { get; }

        /// <summary>True when this port is an input.</summary>
        public bool IsInput => Direction == NodePortDirection.Input;

        /// <summary>True when this port is an output.</summary>
        public bool IsOutput => Direction == NodePortDirection.Output;

        /// <summary>
        /// True when an input port accepts more than one connection.
        /// </summary>
        public bool AllowMultipleConnections { get; }

        /// <summary>
        /// Value used when an input port has no connection. Output ports ignore this.
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>Inclusive lower bound for numeric default values, if any.</summary>
        public double? Min { get; }

        /// <summary>Inclusive upper bound for numeric default values, if any.</summary>
        public double? Max { get; }

        /// <summary>Preferred increment when editing a numeric default value.</summary>
        public double Step { get; }

        /// <summary>Optional editor tooltip.</summary>
        public string? Tooltip { get; }

        /// <summary>The node this port belongs to.</summary>
        public Node Node { get; }

        /// <summary>Runtime type of the default value, falling back to the declared CLR type.</summary>
        public Type? ValueType => DefaultValue?.GetType() ?? DataType.ClrType;

        /// <summary>True when the port has a non-null default value.</summary>
        public bool HasDefaultValue => DefaultValue is not null;

        /// <summary>Clamps a numeric value to this port's range.</summary>
        /// <param name="value">Value to clamp.</param>
        /// <returns>The value restricted to <see cref="Min"/> and <see cref="Max"/>.</returns>
        public double ClampNumeric(double value)
        {
            if (Min is double min && value < min) value = min;
            if (Max is double max && value > max) value = max;
            return value;
        }

        /// <inheritdoc />
        public override string ToString() => $"{Node.Name}.{Name} ({Direction}, {DataType})";
    }

    /// <summary>
    /// A directed link from an output port to an input port.
    /// </summary>
    public sealed class NodeConnection
    {
        internal NodeConnection(string id, NodePort output, NodePort input)
        {
            Id = id;
            Output = output;
            Input = input;
        }

        /// <summary>Stable connection identifier.</summary>
        public string Id { get; }

        /// <summary>Upstream output port.</summary>
        public NodePort Output { get; }

        /// <summary>Downstream input port.</summary>
        public NodePort Input { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Output.Node.Name}.{Output.Name} -> {Input.Node.Name}.{Input.Name}";
    }
}
