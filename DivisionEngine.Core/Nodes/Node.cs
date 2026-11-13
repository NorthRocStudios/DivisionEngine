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
    /// Base class for a node in a <see cref="NodeGraph"/>. Nodes declare typed inputs
    /// and outputs in their constructor, and evaluate themselves when the graph pulls
    /// on one of their outputs.
    /// </summary>
    /// <remarks>
    /// A node is intentionally independent of the editor: the same instance can be
    /// evaluated at runtime, serialized to disk, or wrapped in editor view models.
    /// Input ports carry defaults, so a node can be fully configured by the graph
    /// without any additional inspector fields.
    /// </remarks>
    public abstract class Node
    {
        private readonly List<NodePort> ports = [];
        private readonly List<NodePort> inputs = [];
        private readonly List<NodePort> outputs = [];

        /// <summary>
        /// Creates a node with a default display name derived from its type.
        /// </summary>
        protected Node()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = GetType().Name.EndsWith("Node", StringComparison.Ordinal)
                ? GetType().Name[..^4]
                : GetType().Name;
        }

        /// <summary>
        /// Stable identifier used by serialization and undo. Change this only before
        /// the node is added to a graph.
        /// </summary>
        public string Id { get; set; }

        /// <summary>Display name of the node.</summary>
        public string Name { get; set; }

        /// <summary>Graph-space X position (used by the editor).</summary>
        public double X { get; set; }

        /// <summary>Graph-space Y position (used by the editor).</summary>
        public double Y { get; set; }

        /// <summary>
        /// Graph this node currently belongs to, or null when detached.
        /// </summary>
        public NodeGraph? Graph { get; internal set; }

        /// <summary>All declared input ports, in declaration order.</summary>
        public IReadOnlyList<NodePort> Inputs => inputs;

        /// <summary>All declared output ports, in declaration order.</summary>
        public IReadOnlyList<NodePort> Outputs => outputs;

        /// <summary>Every port on this node, inputs first.</summary>
        public IReadOnlyList<NodePort> Ports => ports;

        /// <summary>
        /// Raised when the node data or port defaults change outside of graph
        /// structure edits. The editor uses it to invalidate previews.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Declares a typed input port whose value can come from a connection or the
        /// supplied default.
        /// </summary>
        /// <typeparam name="T">Value type of the port.</typeparam>
        /// <param name="name">Port name, also used as a stable id.</param>
        /// <param name="defaultValue">Value used when the port is not connected.</param>
        /// <param name="allowMultipleConnections">Whether many outputs may feed this port.</param>
        /// <param name="min">Optional lower bound for numeric values.</param>
        /// <param name="max">Optional upper bound for numeric values.</param>
        /// <param name="step">Preferred editor increment for numeric values; 0 infers one.</param>
        /// <param name="tooltip">Optional editor tooltip.</param>
        /// <returns>The created input port.</returns>
        public NodePort AddInput<T>(
            string name,
            T defaultValue = default!,
            bool allowMultipleConnections = false,
            double? min = null,
            double? max = null,
            double step = 0,
            string? tooltip = null)
            => AddInput(name, NodeDataType.FromType(typeof(T)), defaultValue, allowMultipleConnections, min, max, step, tooltip);

        /// <summary>
        /// Declares an input port with an explicit data type and no default value.
        /// </summary>
        /// <param name="name">Port name, also used as a stable id.</param>
        /// <param name="dataType">Value type of the port.</param>
        /// <returns>The created input port.</returns>
        public NodePort AddInput(string name, NodeDataType dataType)
            => AddInput(name, dataType, null);

        /// <summary>
        /// Declares an input port with an explicit data type. Use this overload for
        /// types that share a CLR type with a different semantic kind, such as colour
        /// and vector4 (both <c>float4</c>).
        /// </summary>
        /// <param name="name">Port name, also used as a stable id.</param>
        /// <param name="dataType">Value type of the port.</param>
        /// <param name="defaultValue">Value used when the port is not connected.</param>
        /// <param name="allowMultipleConnections">Whether many outputs may feed this port.</param>
        /// <param name="min">Optional lower bound for numeric values.</param>
        /// <param name="max">Optional upper bound for numeric values.</param>
        /// <param name="step">Preferred editor increment for numeric values; 0 infers one.</param>
        /// <param name="tooltip">Optional editor tooltip.</param>
        /// <returns>The created input port.</returns>
        public NodePort AddInput(
            string name,
            NodeDataType dataType,
            object? defaultValue = null,
            bool allowMultipleConnections = false,
            double? min = null,
            double? max = null,
            double step = 0,
            string? tooltip = null)
        {
            NodePort port = CreatePort(name, dataType, NodePortDirection.Input, defaultValue,
                allowMultipleConnections, min, max, step, tooltip);
            inputs.Add(port);
            return port;
        }

        /// <summary>
        /// Declares a typed output port.
        /// </summary>
        /// <typeparam name="T">Value type produced by the port.</typeparam>
        /// <param name="name">Port name, also used as a stable id.</param>
        /// <param name="tooltip">Optional editor tooltip.</param>
        /// <returns>The created output port.</returns>
        public NodePort AddOutput<T>(string name, string? tooltip = null)
            => AddOutput(name, NodeDataType.FromType(typeof(T)), tooltip);

        /// <summary>
        /// Declares an output port with an explicit data type.
        /// </summary>
        /// <param name="name">Port name, also used as a stable id.</param>
        /// <param name="dataType">Value type produced by the port.</param>
        /// <param name="tooltip">Optional editor tooltip.</param>
        /// <returns>The created output port.</returns>
        public NodePort AddOutput(string name, NodeDataType dataType, string? tooltip = null)
        {
            NodePort port = CreatePort(name, dataType, NodePortDirection.Output, null,
                false, null, null, 0, tooltip);
            outputs.Add(port);
            return port;
        }

        /// <summary>
        /// Finds a port by name, optionally restricted to one direction.
        /// </summary>
        /// <param name="name">Port name to look for.</param>
        /// <param name="direction">Optional direction filter.</param>
        /// <returns>The matching port, or null.</returns>
        public NodePort? FindPort(string name, NodePortDirection? direction = null)
        {
            foreach (NodePort port in ports)
            {
                if (direction is not null && port.Direction != direction) continue;
                if (string.Equals(port.Name, name, StringComparison.OrdinalIgnoreCase)) return port;
            }
            return null;
        }

        /// <summary>
        /// Finds a port by its stable id.
        /// </summary>
        /// <param name="id">Stable port id.</param>
        /// <returns>The matching port, or null.</returns>
        public NodePort? FindPortById(string id)
        {
            foreach (NodePort port in ports)
                if (string.Equals(port.Id, id, StringComparison.Ordinal)) return port;
            return null;
        }

        /// <summary>
        /// Evaluates the node for one of its output ports.
        /// </summary>
        /// <param name="context">Evaluation context supplying resolved input values.</param>
        /// <param name="output">
        /// Output port being requested, or null when a sink node (no outputs) is
        /// evaluated.
        /// </param>
        /// <returns>The computed value, or null for nodes that only produce side effects.</returns>
        public abstract object? Evaluate(NodeEvaluationContext context, NodePort? output);

        /// <summary>
        /// Called after the node has been added to a graph.
        /// </summary>
        public virtual void OnGraphAttached() { }

        /// <summary>
        /// Called after the node has been removed from a graph.
        /// </summary>
        public virtual void OnGraphDetached() { }

        /// <summary>
        /// Notifies listeners that node data changed (defaults, properties, etc.).
        /// </summary>
        public void NotifyChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
            Graph?.NotifyNodeChanged(this);
        }

        private NodePort CreatePort(
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
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A node port needs a name.", nameof(name));

            string baseId = (direction == NodePortDirection.Input ? "in." : "out.") + name;
            string id = baseId;
            int suffix = 2;
            while (ports.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal)))
                id = baseId + suffix++;

            NodePort port = new(this, id, name, dataType, direction, defaultValue,
                allowMultipleConnections, min, max, step, tooltip);
            ports.Add(port);
            return port;
        }
    }
}
