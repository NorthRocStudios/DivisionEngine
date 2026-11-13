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
using Material.Icons;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// Example node editor window that builds a small SDF graph. It exists to prove the
    /// base window out end-to-end: typed ports, wiring, inline values, an undoable edit
    /// history, serialization and a custom ray-marched preview node. Real editors
    /// (geometry, materials, procedural audio) derive from
    /// <see cref="NodeEditorWindowViewModel"/> in the same way.
    /// </summary>
    public sealed class SdfPlaygroundExampleViewModel : NodeEditorWindowViewModel
    {
        /// <summary>Creates the example graph.</summary>
        public SdfPlaygroundExampleViewModel() : base("SDF Playground (Example)")
        {
            Title = "SDF Playground (Example)";
            Icon = MaterialIconKind.FlaskOutline;
            BuildSampleGraph();
        }

        /// <inheritdoc />
        protected override NodeViewModel CreateNodeViewModel(Node node) => node switch
        {
            SdfPreviewNode => new SdfPreviewNodeViewModel(node, this),
            _ => base.CreateNodeViewModel(node),
        };

        private void BuildSampleGraph()
        {
            NodeGraph graph = new("SDF Playground (Example)");

            FloatNode radiusNode = new()
            {
                Name = "Radius",
                Value = 1.25f,
                X = 40,
                Y = 100,
            };
            graph.AddNode(radiusNode);

            FloatNode smoothnessNode = new()
            {
                Name = "Smoothness",
                Value = 0.16f,
                X = 40,
                Y = 470,
            };
            graph.AddNode(smoothnessNode);

            SdfSphereNode sphereNode = new()
            {
                Name = "Sphere",
                X = 350,
                Y = 80,
            };
            sphereNode.FindPort("Radius", NodePortDirection.Input)!.DefaultValue = 1.0f;
            graph.AddNode(sphereNode);

            SdfBoxNode boxNode = new()
            {
                Name = "Box",
                X = 350,
                Y = 310,
            };
            boxNode.FindPort("Size", NodePortDirection.Input)!.DefaultValue = new float3(0.7f, 0.7f, 0.7f);
            boxNode.FindPort("Rounding", NodePortDirection.Input)!.DefaultValue = 0.08f;
            boxNode.FindPort("Color", NodePortDirection.Input)!.DefaultValue = new float4(0.32f, 0.68f, 1f, 1f);
            graph.AddNode(boxNode);

            SdfUnionNode unionNode = new()
            {
                Name = "Union",
                X = 670,
                Y = 170,
            };
            unionNode.FindPort("Smoothness", NodePortDirection.Input)!.DefaultValue = 0.16f;
            graph.AddNode(unionNode);

            SdfPreviewNode previewNode = new()
            {
                Name = "Preview",
                X = 990,
                Y = 190,
            };
            graph.AddNode(previewNode);

            graph.TryConnect(radiusNode.FindPort("Value", NodePortDirection.Output),
                sphereNode.FindPort("Radius", NodePortDirection.Input), out _, out _);
            graph.TryConnect(smoothnessNode.FindPort("Value", NodePortDirection.Output),
                unionNode.FindPort("Smoothness", NodePortDirection.Input), out _, out _);
            graph.TryConnect(sphereNode.FindPort("SDF", NodePortDirection.Output),
                unionNode.FindPort("A", NodePortDirection.Input), out _, out _);
            graph.TryConnect(boxNode.FindPort("SDF", NodePortDirection.Output),
                unionNode.FindPort("B", NodePortDirection.Input), out _, out _);
            graph.TryConnect(unionNode.FindPort("SDF", NodePortDirection.Output),
                previewNode.FindPort("SDF", NodePortDirection.Input), out _, out _);

            SetGraphModel(graph, markDirty: false, resetHistory: true);
            StatusText = "Example graph - right-click the canvas or press Space to add a node. Drag a port to connect.";
        }
    }
}
