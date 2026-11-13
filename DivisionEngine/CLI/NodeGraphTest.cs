//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Nodes;
using DivisionEngine.Nodes.Math;
using DivisionEngine.Nodes.Sdf;
using DivisionEngine.Nodes.Values;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DivisionEngine.Editor
{
    /// <summary>
    /// Headless regression checks for the node graph model: registry discovery, typed
    /// connections, evaluation, cycle rejection and serialization round trips. Run with
    /// <c>DivisionEngine.Editor.exe --cli nodetest</c>.
    /// </summary>
    internal static class NodeGraphTest
    {
        private static int passed;
        private static int failed;

        /// <summary>Runs the node graph checks.</summary>
        /// <param name="args">CLI arguments (unused).</param>
        /// <returns>True when the command was handled.</returns>
        public static bool Run(string[] args)
        {
            passed = 0;
            failed = 0;
            Console.WriteLine("divisioncli nodetest: node graph model checks");

            Check("registry discovers built-in nodes", RegistryDiscoversNodes);
            Check("float + add evaluates", AddEvaluates);
            Check("unconnected inputs use defaults", InputDefaultsEvaluate);
            Check("type incompatible connections are rejected", TypeCompatibilityIsEnforced);
            Check("cycles are rejected", CyclesAreRejected);
            Check("single input connections are replaced", InputsAreReplaced);
            Check("port defaults coerce and clamp", PortDefaultsClamp);
            Check("SDF nodes compose and evaluate", SdfGraphEvaluates);
            Check("graph evaluation reports no errors", EvaluateAllSucceeds);
            Check("serialization round trip preserves the graph", SerializationRoundTrips);
            Check("serialization preserves node properties", PropertiesRoundTrip);
            Check("clone deep copies the graph", CloneCopiesGraph);
            Check("unknown node types fail to load", UnknownTypesFail);

            Console.WriteLine();
            Console.WriteLine($"nodetest: {passed} passed, {failed} failed");
            DivisionCLI.SetExitCode(failed == 0 ? 0 : 1);
            return true;
        }

        private static void Check(string name, Func<(bool Success, string? Detail)> test)
        {
            try
            {
                (bool success, string? detail) = test();
                if (success)
                {
                    passed++;
                    Console.WriteLine($"  PASS  {name}");
                }
                else
                {
                    failed++;
                    Console.WriteLine($"  FAIL  {name}: {detail ?? "no detail"}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}: {ex}");
            }
        }

        private static (bool, string?) RegistryDiscoversNodes()
        {
            IReadOnlyList<NodeTypeRegistration> all = NodeTypeRegistry.All;
            string[] expected = ["FloatNode", "AddNode", "SdfSphereNode", "SdfBoxNode", "SdfUnionNode", "SdfPreviewNode"];
            string[] missing = expected
                .Where(typeName => !all.Any(r => r.NodeType.Name == typeName))
                .ToArray();
            if (missing.Length > 0) return (false, $"missing registrations: {string.Join(", ", missing)}");

            NodeTypeRegistration? add = all.First(r => r.NodeType == typeof(AddNode));
            if (add.MenuPath != "Math/Add") return (false, $"unexpected menu path {add.MenuPath}");
            if (!NodeTypeRegistry.TryCreate(add.TypeId, out Node? node, out string? error))
                return (false, error);
            return (node is AddNode, null);
        }

        private static (bool, string?) AddEvaluates()
        {
            NodeGraph graph = new("add-test");
            FloatNode a = new() { Value = 2.5f };
            FloatNode b = new() { Value = 3.25f };
            AddNode add = new();
            graph.AddNode(a);
            graph.AddNode(b);
            graph.AddNode(add);

            if (!graph.TryConnect(a.Outputs[0], add.Inputs[0], out _, out string? error))
                return (false, error);
            if (!graph.TryConnect(b.Outputs[0], add.Inputs[1], out _, out error))
                return (false, error);

            if (!graph.TryEvaluateOutput(add.Outputs[0], out object? value, out error))
                return (false, error);
            return (value is float result && MathF.Abs(result - 5.75f) < 1e-5f,
                $"expected 5.75, got {value}");
        }

        private static (bool, string?) InputDefaultsEvaluate()
        {
            NodeGraph graph = new("defaults");
            AddNode add = new();
            graph.AddNode(add);
            add.Inputs[0].DefaultValue = 1.5f;
            add.Inputs[1].DefaultValue = 2f;
            if (!graph.TryEvaluateOutput(add.Outputs[0], out object? value, out string? error))
                return (false, error);
            return (value is float result && MathF.Abs(result - 3.5f) < 1e-5f, $"expected 3.5, got {value}");
        }

        private static (bool, string?) TypeCompatibilityIsEnforced()
        {
            NodeGraph graph = new("types");
            ColorNode color = new();
            AddNode add = new();
            graph.AddNode(color);
            graph.AddNode(add);
            bool connected = graph.TryConnect(color.Outputs[0], add.Inputs[0], out _, out string? error);
            return (!connected && error is not null && error.Contains("cannot connect", StringComparison.OrdinalIgnoreCase),
                $"expected a type error, got connected={connected}, error={error}");
        }

        private static (bool, string?) CyclesAreRejected()
        {
            NodeGraph graph = new("cycles");
            AddNode first = new();
            AddNode second = new();
            graph.AddNode(first);
            graph.AddNode(second);
            if (!graph.TryConnect(first.Outputs[0], second.Inputs[0], out _, out string? error))
                return (false, error);
            bool connected = graph.TryConnect(second.Outputs[0], first.Inputs[0], out _, out error);
            return (!connected && error is not null && error.Contains("cycle", StringComparison.OrdinalIgnoreCase),
                $"expected a cycle error, got connected={connected}, error={error}");
        }

        private static (bool, string?) InputsAreReplaced()
        {
            NodeGraph graph = new("replace");
            FloatNode first = new() { Value = 1f };
            FloatNode second = new() { Value = 9f };
            AddNode add = new();
            graph.AddNode(first);
            graph.AddNode(second);
            graph.AddNode(add);
            graph.TryConnect(first.Outputs[0], add.Inputs[0], out _, out _);
            graph.TryConnect(second.Outputs[0], add.Inputs[0], out _, out _);
            if (graph.GetInputConnections(add.Inputs[0]).Count != 1)
                return (false, "expected exactly one connection on the input");
            if (!graph.TryEvaluateOutput(add.Outputs[0], out object? value, out string? error))
                return (false, error);
            return (value is float result && MathF.Abs(result - 9f) < 1e-5f, $"expected 9, got {value}");
        }

        private static (bool, string?) PortDefaultsClamp()
        {
            NodeGraph graph = new("clamp");
            SdfSphereNode sphere = new();
            graph.AddNode(sphere);
            NodePort radius = sphere.FindPort("Radius", NodePortDirection.Input)!;
            return (Math.Abs(radius.ClampNumeric(-5) - 0.001) < 1e-5, $"clamp gave {radius.ClampNumeric(-5)}");
        }

        private static (bool, string?) SdfGraphEvaluates()
        {
            NodeGraph graph = new("sdf");
            SdfSphereNode sphere = new();
            SdfBoxNode box = new();
            SdfUnionNode union = new();
            SdfPreviewNode preview = new();
            graph.AddNode(sphere);
            graph.AddNode(box);
            graph.AddNode(union);
            graph.AddNode(preview);

            sphere.FindPort("Radius", NodePortDirection.Input)!.DefaultValue = 1.5f;
            if (graph.TryConnect(sphere.Outputs[0], union.Inputs[0], out _, out string? error) is false)
                return (false, error);
            if (graph.TryConnect(box.Outputs[0], union.Inputs[1], out _, out error) is false)
                return (false, error);
            if (graph.TryConnect(union.Outputs[0], preview.Inputs[0], out _, out error) is false)
                return (false, error);

            if (!graph.TryEvaluateNode(preview, out object? value, out error)) return (false, error);
            if (value is not SdfShape shape) return (false, $"expected an SdfShape, got {value?.GetType().Name ?? "null"}");

            float surface = shape.Evaluate(new float3(1.5f, 0f, 0f));
            if (MathF.Abs(surface) > 0.2f) return (false, $"expected a surface near x=1.5, distance was {surface}");
            if (!shape.Description.Contains("Union", StringComparison.Ordinal))
                return (false, $"unexpected description {shape.Description}");

            float outside = shape.Evaluate(new float3(0f, 0f, 3f));
            return (outside > 0.1f, $"expected positive distance outside, got {outside}");
        }

        private static (bool, string?) EvaluateAllSucceeds()
        {
            NodeGraph graph = new("all");
            FloatNode a = new() { Value = 1f };
            FloatNode b = new() { Value = 2f };
            AddNode add = new();
            graph.AddNode(a);
            graph.AddNode(b);
            graph.AddNode(add);
            graph.TryConnect(a.Outputs[0], add.Inputs[0], out _, out _);
            graph.TryConnect(b.Outputs[0], add.Inputs[1], out _, out _);

            NodeGraphEvaluationResult result = graph.EvaluateAll();
            return (result.Succeeded && result.NodesEvaluated == 3,
                $"success={result.Succeeded}, nodes={result.NodesEvaluated}, errors={string.Join("; ", result.Errors)}");
        }

        private static (bool, string?) SerializationRoundTrips()
        {
            NodeGraph graph = BuildMixedGraph();
            string json = NodeGraphSerializer.Serialize(graph);
            if (!json.Contains("\"version\"", StringComparison.Ordinal))
                return (false, "serialized payload has no version field");

            NodeGraph restored = NodeGraphSerializer.Deserialize(json);
            if (restored.Nodes.Count != graph.Nodes.Count)
                return (false, $"node count {restored.Nodes.Count} != {graph.Nodes.Count}");
            if (restored.Connections.Count != graph.Connections.Count)
                return (false, $"connection count {restored.Connections.Count} != {graph.Connections.Count}");

            Node? restoredAdd = restored.Nodes.FirstOrDefault(n => n is AddNode);
            if (restoredAdd is null) return (false, "AddNode missing after load");
            if (!restored.TryEvaluateOutput(restoredAdd.Outputs[0], out object? value, out string? error))
                return (false, error);
            if (value is not float result || MathF.Abs(result - 5.75f) > 1e-5f)
                return (false, $"expected 5.75 after load, got {value}");

            Node? restoredPreview = restored.Nodes.FirstOrDefault(n => n is SdfPreviewNode);
            if (restoredPreview is null) return (false, "SdfPreviewNode missing after load");
            if (!restored.TryEvaluateNode(restoredPreview, out object? shapeValue, out error))
                return (false, error);
            return (shapeValue is SdfShape, $"expected an SdfShape after load, got {shapeValue?.GetType().Name ?? "null"}");
        }

        private static (bool, string?) PropertiesRoundTrip()
        {
            NodeGraph graph = new("properties");
            FloatNode constant = new() { Value = 7.25f, X = 12, Y = 34 };
            graph.AddNode(constant);
            NodeGraph restored = NodeGraphSerializer.Deserialize(NodeGraphSerializer.Serialize(graph));
            FloatNode? loaded = restored.Nodes.OfType<FloatNode>().FirstOrDefault();
            if (loaded is null) return (false, "FloatNode missing");
            return (MathF.Abs(loaded.Value - 7.25f) < 1e-5f && Math.Abs(loaded.X - 12) < 1e-6,
                $"value={loaded.Value}, x={loaded.X}");
        }

        private static (bool, string?) CloneCopiesGraph()
        {
            NodeGraph graph = BuildMixedGraph();
            NodeGraph clone = NodeGraphSerializer.Clone(graph);
            if (clone.Nodes.Count != graph.Nodes.Count) return (false, "node count mismatch");
            if (ReferenceEquals(clone.Nodes[0], graph.Nodes[0])) return (false, "clone shares node instances");
            return (true, null);
        }

        private static (bool, string?) UnknownTypesFail()
        {
            const string json = """
                {
                  "version": 1,
                  "id": "test",
                  "name": "test",
                  "nodes": [
                    { "id": "1", "type": "Example.DoesNotExist", "name": "Missing", "x": 0, "y": 0, "inputs": [], "properties": {} }
                  ],
                  "connections": []
                }
                """;
            try
            {
                NodeGraphSerializer.Deserialize(json);
                return (false, "expected a NodeGraphSerializationException");
            }
            catch (NodeGraphSerializationException ex)
            {
                return (ex.Details.Any(d => d.Contains("Unknown node type", StringComparison.OrdinalIgnoreCase)), ex.Message);
            }
        }

        private static NodeGraph BuildMixedGraph()
        {
            NodeGraph graph = new("mixed");
            FloatNode a = new() { Value = 2.5f, X = 10, Y = 20 };
            FloatNode b = new() { Value = 3.25f, X = 10, Y = 100 };
            AddNode add = new() { X = 300, Y = 50 };
            SdfSphereNode sphere = new() { X = 300, Y = 240 };
            SdfPreviewNode preview = new() { X = 640, Y = 240 };
            sphere.FindPort("Radius", NodePortDirection.Input)!.DefaultValue = 1.25f;
            graph.AddNode(a);
            graph.AddNode(b);
            graph.AddNode(add);
            graph.AddNode(sphere);
            graph.AddNode(preview);
            graph.TryConnect(a.Outputs[0], add.Inputs[0], out _, out _);
            graph.TryConnect(b.Outputs[0], add.Inputs[1], out _, out _);
            graph.TryConnect(sphere.Outputs[0], preview.Inputs[0], out _, out _);
            return graph;
        }
    }
}
