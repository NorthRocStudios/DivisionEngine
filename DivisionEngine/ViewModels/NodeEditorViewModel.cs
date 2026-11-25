//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//

//namespace DivisionEngine.Editor.ViewModels
//{
//    public partial class NodeEditorWindowViewModel : EditorWindowViewModel
//    {
//        [ObservableProperty]
//        private NodeGraph _graph;

//        [ObservableProperty]
//        private ObservableCollection<Node> _nodes;

//        public NodeEditorViewModel()
//        {
//            Title = "Node Editor";
//            Icon = Material.Icons.MaterialIconKind.GraphOutline;

//            // Create a simple test graph
//            _graph = new NodeGraph("Test Graph");
//            _nodes = new ObservableCollection<Node>();

//            // Add some test nodes
//            var floatNode = new FloatNode { Value = 5f, Name = "Value A", X = 100, Y = 100 };
//            var addNode = new AddNode { Name = "Add", X = 350, Y = 100 };

//            _graph.AddNode(floatNode);
//            _graph.AddNode(addNode);

//            // Sync to observable collection for UI
//            _nodes.Add(floatNode);
//            _nodes.Add(addNode);
//        }

//        [RelayCommand]
//        private void AddFloat()
//        {
//            var floatNode = new FloatNode
//            {
//                Value = 0f,
//                Name = $"Float {Nodes.Count + 1}",
//                X = 100,
//                Y = 100 + (Nodes.Count * 80)
//            };

//            _graph.AddNode(floatNode);
//            Nodes.Add(floatNode);
//        }
//    }
//}
