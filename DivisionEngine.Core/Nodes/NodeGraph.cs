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
    /// Kinds of structural change reported by <see cref="NodeGraph.Changed"/>.
    /// </summary>
    public enum NodeGraphChangeKind
    {
        /// <summary>A node was added.</summary>
        NodeAdded,
        /// <summary>A node was removed (along with its connections).</summary>
        NodeRemoved,
        /// <summary>A connection was added.</summary>
        ConnectionAdded,
        /// <summary>A connection was removed.</summary>
        ConnectionRemoved,
        /// <summary>Node data changed without a structural edit.</summary>
        NodeChanged,
        /// <summary>The whole graph content was replaced.</summary>
        GraphReloaded,
    }

    /// <summary>
    /// Describes a graph structure change.
    /// </summary>
    public sealed class NodeGraphChangedEventArgs : EventArgs
    {
        /// <summary>Creates event arguments for a graph change.</summary>
        /// <param name="kind">Kind of change.</param>
        /// <param name="node">Node involved, if any.</param>
        /// <param name="connection">Connection involved, if any.</param>
        public NodeGraphChangedEventArgs(NodeGraphChangeKind kind, Node? node = null, NodeConnection? connection = null)
        {
            Kind = kind;
            Node = node;
            Connection = connection;
        }

        /// <summary>Kind of change.</summary>
        public NodeGraphChangeKind Kind { get; }

        /// <summary>Node involved, if any.</summary>
        public Node? Node { get; }

        /// <summary>Connection involved, if any.</summary>
        public NodeConnection? Connection { get; }
    }

    /// <summary>
    /// Result of evaluating every node in a graph.
    /// </summary>
    public sealed class NodeGraphEvaluationResult
    {
        internal NodeGraphEvaluationResult(int nodesEvaluated, IReadOnlyList<string> errors, TimeSpan duration)
        {
            NodesEvaluated = nodesEvaluated;
            Errors = errors;
            Duration = duration;
        }

        /// <summary>Number of nodes visited.</summary>
        public int NodesEvaluated { get; }

        /// <summary>Evaluation errors, empty when successful.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Whether any node failed to evaluate.</summary>
        public bool Succeeded => Errors.Count == 0;

        /// <summary>Time spent evaluating.</summary>
        public TimeSpan Duration { get; }
    }

    /// <summary>
    /// A directed graph of <see cref="Node"/> instances connected through typed ports.
    /// The graph validates connections (type compatibility, single-input replacement
    /// and cycle prevention) and provides cached, cycle-safe evaluation.
    /// </summary>
    public sealed class NodeGraph
    {
        private readonly List<Node> nodes = [];
        private readonly List<NodeConnection> connections = [];

        /// <summary>
        /// Creates an empty graph.
        /// </summary>
        /// <param name="name">Display name of the graph.</param>
        public NodeGraph(string name = "Node Graph")
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
        }

        /// <summary>Stable graph identifier.</summary>
        public string Id { get; set; }

        /// <summary>Display name of the graph.</summary>
        public string Name { get; set; }

        /// <summary>Nodes in the graph, in editor stacking order.</summary>
        public IReadOnlyList<Node> Nodes => nodes;

        /// <summary>All connections in the graph.</summary>
        public IReadOnlyList<NodeConnection> Connections => connections;

        /// <summary>
        /// Raised whenever nodes or connections are added or removed, or node data
        /// changes through <see cref="NotifyNodeChanged"/>.
        /// </summary>
        public event EventHandler<NodeGraphChangedEventArgs>? Changed;

        /// <summary>
        /// Adds a node to the graph.
        /// </summary>
        /// <param name="node">Node to add.</param>
        /// <returns>The added node.</returns>
        public Node AddNode(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.Graph is not null)
                throw new InvalidOperationException($"Node '{node.Name}' already belongs to a graph.");
            if (nodes.Any(n => string.Equals(n.Id, node.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"A node with id '{node.Id}' is already in the graph.");

            node.Graph = this;
            nodes.Add(node);
            node.OnGraphAttached();
            RaiseChanged(new NodeGraphChangedEventArgs(NodeGraphChangeKind.NodeAdded, node));
            return node;
        }

        /// <summary>
        /// Removes a node and every connection touching it.
        /// </summary>
        /// <param name="node">Node to remove.</param>
        /// <returns>True when the node was found and removed.</returns>
        public bool RemoveNode(Node node)
        {
            if (node is null || !nodes.Contains(node)) return false;

            foreach (NodeConnection connection in connections.Where(c => c.Output.Node == node || c.Input.Node == node).ToArray())
                RemoveConnectionCore(connection);

            nodes.Remove(node);
            node.Graph = null;
            node.OnGraphDetached();
            RaiseChanged(new NodeGraphChangedEventArgs(NodeGraphChangeKind.NodeRemoved, node));
            return true;
        }

        /// <summary>
        /// Removes a node by id, along with every connection touching it.
        /// </summary>
        /// <param name="nodeId">Id of the node to remove.</param>
        /// <returns>True when a node was found and removed.</returns>
        public bool RemoveNode(string nodeId)
        {
            Node? node = nodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));
            return node is not null && RemoveNode(node);
        }

        /// <summary>Finds a node by id.</summary>
        /// <param name="nodeId">Node id to look for.</param>
        public Node? FindNode(string nodeId)
            => nodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));

        /// <summary>
        /// Checks whether two ports may be connected and explains why not.
        /// </summary>
        /// <param name="output">Candidate upstream output port.</param>
        /// <param name="input">Candidate downstream input port.</param>
        /// <param name="reason">Human readable failure reason.</param>
        /// <returns>True when a connection is legal.</returns>
        public bool CanConnect(NodePort? output, NodePort? input, out string? reason)
        {
            reason = null;
            if (output is null || input is null)
            {
                reason = "Both ports are required.";
                return false;
            }

            if (output.Direction != NodePortDirection.Output)
            {
                reason = "Connections start at an output port.";
                return false;
            }

            if (input.Direction != NodePortDirection.Input)
            {
                reason = "Connections end at an input port.";
                return false;
            }

            if (output.Node.Graph != this || input.Node.Graph != this)
            {
                reason = "Both ports must belong to this graph.";
                return false;
            }

            if (ReferenceEquals(output.Node, input.Node))
            {
                reason = "A node cannot connect to itself.";
                return false;
            }

            if (!output.DataType.IsCompatibleWith(input.DataType))
            {
                reason = $"{output.DataType.DisplayName} cannot connect to {input.DataType.DisplayName}.";
                return false;
            }

            if (WouldCreateCycle(output, input))
            {
                reason = "That connection would create a cycle.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to connect an output to an input port.
        /// </summary>
        /// <param name="output">Upstream output port.</param>
        /// <param name="input">Downstream input port.</param>
        /// <param name="connection">The created (or existing) connection on success.</param>
        /// <param name="error">Failure reason on failure.</param>
        /// <returns>True when the ports are connected.</returns>
        public bool TryConnect(NodePort? output, NodePort? input, out NodeConnection? connection, out string? error)
        {
            connection = null;
            if (!CanConnect(output, input, out error)) return false;
            NodePort outputPort = output!;
            NodePort inputPort = input!;

            // An input that only accepts one connection replaces its current link.
            foreach (NodeConnection existing in connections
                         .Where(c => c.Input == inputPort && (!inputPort.AllowMultipleConnections || c.Output == outputPort))
                         .ToArray())
                RemoveConnectionCore(existing);

            NodeConnection? already = connections.FirstOrDefault(c => c.Output == outputPort && c.Input == inputPort);
            if (already is not null)
            {
                connection = already;
                return true;
            }

            NodeConnection created = new(Guid.NewGuid().ToString("N"), outputPort, inputPort);
            connections.Add(created);
            RaiseChanged(new NodeGraphChangedEventArgs(NodeGraphChangeKind.ConnectionAdded, null, created));
            connection = created;
            return true;
        }

        /// <summary>
        /// Removes a connection.
        /// </summary>
        /// <param name="connection">Connection to remove.</param>
        /// <returns>True when the connection was found and removed.</returns>
        public bool RemoveConnection(NodeConnection connection)
        {
            if (connection is null || !connections.Contains(connection)) return false;
            RemoveConnectionCore(connection);
            return true;
        }

        /// <summary>
        /// Removes every connection feeding an input port.
        /// </summary>
        /// <param name="input">Input port to disconnect.</param>
        /// <returns>Number of connections removed.</returns>
        public int DisconnectInput(NodePort input)
        {
            NodeConnection[] matches = connections.Where(c => c.Input == input).ToArray();
            foreach (NodeConnection connection in matches) RemoveConnectionCore(connection);
            return matches.Length;
        }

        /// <summary>
        /// Returns the connection feeding an input port, if any.
        /// </summary>
        /// <param name="input">Input port to inspect.</param>
        public NodeConnection? GetInputConnection(NodePort input)
            => connections.FirstOrDefault(c => c.Input == input);

        /// <summary>
        /// Returns every connection feeding an input port.
        /// </summary>
        /// <param name="input">Input port to inspect.</param>
        public IReadOnlyList<NodeConnection> GetInputConnections(NodePort input)
            => connections.Where(c => c.Input == input).ToArray();

        /// <summary>
        /// Returns every connection leaving an output port.
        /// </summary>
        /// <param name="output">Output port to inspect.</param>
        public IReadOnlyList<NodeConnection> GetOutputConnections(NodePort output)
            => connections.Where(c => c.Output == output).ToArray();

        /// <summary>
        /// Returns every connection touching a port.
        /// </summary>
        /// <param name="port">Port to inspect.</param>
        public IReadOnlyList<NodeConnection> GetConnections(NodePort port)
            => port.IsInput ? GetInputConnections(port) : GetOutputConnections(port);

        /// <summary>
        /// Returns true when an input port is fed by at least one connection.
        /// </summary>
        /// <param name="input">Input port to inspect.</param>
        public bool IsInputConnected(NodePort input) => connections.Any(c => c.Input == input);

        /// <summary>
        /// Returns true when an output port feeds at least one input.
        /// </summary>
        /// <param name="output">Output port to inspect.</param>
        public bool IsOutputConnected(NodePort output) => connections.Any(c => c.Output == output);

        /// <summary>
        /// Notifies listeners that a node's data changed (without a structural edit).
        /// </summary>
        /// <param name="node">Node that changed.</param>
        public void NotifyNodeChanged(Node node)
        {
            if (node.Graph == this)
                RaiseChanged(new NodeGraphChangedEventArgs(NodeGraphChangeKind.NodeChanged, node));
        }

        /// <summary>
        /// Raises the graph reloaded event. Called by loaders after replacing content.
        /// </summary>
        public void NotifyGraphReloaded()
            => RaiseChanged(new NodeGraphChangedEventArgs(NodeGraphChangeKind.GraphReloaded));

        /// <summary>
        /// Orders nodes so that every node comes after the nodes feeding it. Nodes that
        /// participate in a cycle (which the connection rules prevent) are omitted.
        /// </summary>
        /// <param name="cyclicNodes">Nodes that could not be ordered.</param>
        public IReadOnlyList<Node> GetTopologicalOrder(out IReadOnlyList<Node> cyclicNodes)
        {
            Dictionary<Node, HashSet<Node>> dependencies = [];
            foreach (Node node in nodes) dependencies[node] = [];

            foreach (NodeConnection connection in connections)
            {
                if (!dependencies.TryGetValue(connection.Input.Node, out HashSet<Node>? set)) continue;
                set.Add(connection.Output.Node);
            }

            List<Node> ordered = [];
            HashSet<Node> remaining = [.. nodes];
            bool progressed = true;
            while (progressed && remaining.Count > 0)
            {
                progressed = false;
                foreach (Node node in remaining.ToArray())
                {
                    if (dependencies[node].All(n => !remaining.Contains(n)))
                    {
                        ordered.Add(node);
                        remaining.Remove(node);
                        progressed = true;
                    }
                }
            }

            cyclicNodes = [.. remaining];
            return ordered;
        }

        /// <summary>
        /// Evaluates every node in topological order.
        /// </summary>
        public NodeGraphEvaluationResult EvaluateAll()
        {
            DateTime start = DateTime.UtcNow;
            List<string> errors = [];
            Dictionary<Node, Dictionary<string, object?>> cache = [];
            IReadOnlyList<Node> order = GetTopologicalOrder(out IReadOnlyList<Node> cyclicNodes);
            foreach (Node node in cyclicNodes)
                errors.Add($"{node.Name}: skipped because the graph contains a cycle.");

            int evaluated = 0;
            foreach (Node node in order)
            {
                foreach (NodePort output in node.Outputs)
                    EvaluatePort(output, cache, [], errors);
                if (node.Outputs.Count == 0)
                    EvaluatePortInternal(node, null, cache, [], errors);
                evaluated++;
            }

            return new NodeGraphEvaluationResult(evaluated, errors, DateTime.UtcNow - start);
        }

        /// <summary>
        /// Evaluates one output port.
        /// </summary>
        /// <param name="output">Output port to evaluate.</param>
        /// <param name="value">Computed value.</param>
        /// <param name="error">Aggregated errors, or null.</param>
        /// <returns>True when evaluation succeeded.</returns>
        public bool TryEvaluateOutput(NodePort? output, out object? value, out string? error)
        {
            value = null;
            error = null;
            if (output is null || output.Direction != NodePortDirection.Output)
            {
                error = "An output port is required.";
                return false;
            }

            if (output.Node.Graph != this)
            {
                error = "The port does not belong to this graph.";
                return false;
            }

            List<string> errors = [];
            Dictionary<Node, Dictionary<string, object?>> cache = [];
            value = EvaluatePort(output, cache, [], errors);
            if (errors.Count > 0)
            {
                error = string.Join(Environment.NewLine, errors);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Evaluates a node. Nodes with outputs return their first output; sink nodes
        /// evaluate their inputs and return whatever the node itself produces.
        /// </summary>
        /// <param name="node">Node to evaluate.</param>
        /// <param name="value">Computed value.</param>
        /// <param name="error">Aggregated errors, or null.</param>
        /// <returns>True when evaluation succeeded.</returns>
        public bool TryEvaluateNode(Node? node, out object? value, out string? error)
        {
            value = null;
            error = null;
            if (node is null || node.Graph != this)
            {
                error = "The node does not belong to this graph.";
                return false;
            }

            List<string> errors = [];
            Dictionary<Node, Dictionary<string, object?>> cache = [];
            value = node.Outputs.Count > 0
                ? EvaluatePort(node.Outputs[0], cache, [], errors)
                : EvaluatePortInternal(node, null, cache, [], errors);

            if (errors.Count > 0)
            {
                error = string.Join(Environment.NewLine, errors);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Resolves and evaluates the value feeding an input port. Unconnected inputs
        /// evaluate to their default value.
        /// </summary>
        /// <param name="input">Input port to evaluate.</param>
        /// <param name="value">Resolved value.</param>
        /// <param name="error">Aggregated errors, or null.</param>
        /// <returns>True when evaluation succeeded.</returns>
        public bool TryEvaluateInput(NodePort? input, out object? value, out string? error)
        {
            value = null;
            error = null;
            if (input is null || input.Direction != NodePortDirection.Input)
            {
                error = "An input port is required.";
                return false;
            }

            List<string> errors = [];
            Dictionary<Node, Dictionary<string, object?>> cache = [];
            value = ResolveInput(input, cache, [], errors);
            if (errors.Count > 0)
            {
                error = string.Join(Environment.NewLine, errors);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Legacy entry point: evaluates every node and returns the first output of the
        /// first node, or null.
        /// </summary>
        public object? Evaluate()
        {
            EvaluateAll();
            foreach (Node node in nodes)
                foreach (NodePort output in node.Outputs)
                    if (TryEvaluateOutput(output, out object? value, out _)) return value;
            return null;
        }

        private object? EvaluatePort(
            NodePort output,
            Dictionary<Node, Dictionary<string, object?>> cache,
            HashSet<Node> visiting,
            List<string> errors)
        {
            if (output.Direction != NodePortDirection.Output) return output.DefaultValue;
            return EvaluatePortInternal(output.Node, output, cache, visiting, errors);
        }

        private object? EvaluatePortInternal(
            Node node,
            NodePort? output,
            Dictionary<Node, Dictionary<string, object?>> cache,
            HashSet<Node> visiting,
            List<string> errors)
        {
            if (!cache.TryGetValue(node, out Dictionary<string, object?>? nodeCache))
            {
                nodeCache = [];
                cache[node] = nodeCache;
            }

            string cacheKey = output?.Id ?? "<sink>";
            if (nodeCache.TryGetValue(cacheKey, out object? cached)) return cached;

            if (!visiting.Add(node))
            {
                errors.Add($"{node.Name}: cycle detected during evaluation.");
                return null;
            }

            try
            {
                Dictionary<string, IReadOnlyList<object?>> inputValues = [];
                HashSet<string> connected = [];
                foreach (NodePort input in node.Inputs)
                {
                    IReadOnlyList<NodeConnection> links = GetInputConnections(input);
                    if (links.Count > 0) connected.Add(input.Name);

                    if (links.Count == 0)
                    {
                        inputValues[input.Name] = [input.DefaultValue];
                        continue;
                    }

                    List<object?> values = [];
                    foreach (NodeConnection link in links)
                        values.Add(EvaluatePort(link.Output, cache, visiting, errors));
                    inputValues[input.Name] = values;
                }

                NodeEvaluationContext context = new(node, this, inputValues, connected);
                object? result = node.Evaluate(context, output);
                nodeCache[cacheKey] = result;
                return result;
            }
            catch (Exception ex)
            {
                errors.Add($"{node.Name}: {ex.Message}");
                nodeCache[cacheKey] = null;
                return null;
            }
            finally
            {
                visiting.Remove(node);
            }
        }

        private object? ResolveInput(
            NodePort input,
            Dictionary<Node, Dictionary<string, object?>> cache,
            HashSet<Node> visiting,
            List<string> errors)
        {
            IReadOnlyList<NodeConnection> links = GetInputConnections(input);
            if (links.Count == 0) return input.DefaultValue;
            return EvaluatePort(links[0].Output, cache, visiting, errors);
        }

        private bool WouldCreateCycle(NodePort output, NodePort input)
        {
            // Walk downstream from the proposed input node. If the proposed output node
            // is reachable, adding this edge closes a cycle.
            Stack<Node> pending = new();
            HashSet<Node> visited = [];
            pending.Push(input.Node);
            while (pending.Count > 0)
            {
                Node current = pending.Pop();
                if (!visited.Add(current)) continue;
                if (ReferenceEquals(current, output.Node)) return true;

                foreach (NodeConnection connection in connections)
                {
                    if (!ReferenceEquals(connection.Output.Node, current)) continue;
                    if (visited.Contains(connection.Input.Node)) continue;
                    pending.Push(connection.Input.Node);
                }
            }
            return false;
        }

        private void RemoveConnectionCore(NodeConnection connection)
        {
            if (!connections.Remove(connection)) return;
            RaiseChanged(new NodeGraphChangedEventArgs(NodeGraphChangeKind.ConnectionRemoved, null, connection));
        }

        private void RaiseChanged(NodeGraphChangedEventArgs args) => Changed?.Invoke(this, args);

        /// <summary>
        /// Removes every node and connection without raising per-item events.
        /// </summary>
        internal void Clear()
        {
            connections.Clear();
            foreach (Node node in nodes) node.Graph = null;
            nodes.Clear();
        }
    }
}
