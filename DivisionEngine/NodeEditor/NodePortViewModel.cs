//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Media;
using DivisionEngine.Nodes;
using ReactiveUI;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// Editor wrapper around a <see cref="NodePort"/>. Provides layout anchors and the
    /// state needed by the canvas to draw and hit-test ports.
    /// </summary>
    public sealed class NodePortViewModel : ReactiveObject
    {
        internal NodePortViewModel(NodeViewModel node, NodePort port)
        {
            Node = node;
            Model = port;
        }

        /// <summary>Owning node wrapper.</summary>
        public NodeViewModel Node { get; }

        /// <summary>Model port.</summary>
        public NodePort Model { get; }

        /// <summary>Display name.</summary>
        public string Name => Model.Name;

        /// <summary>Stable port id.</summary>
        public string Id => Model.Id;

        /// <summary>Port direction.</summary>
        public NodePortDirection Direction => Model.Direction;

        /// <summary>True for input ports.</summary>
        public bool IsInput => Model.IsInput;

        /// <summary>True for output ports.</summary>
        public bool IsOutput => Model.IsOutput;

        /// <summary>Value kind.</summary>
        public NodeValueKind Kind => Model.DataType.Kind;

        /// <summary>Full data type.</summary>
        public NodeDataType DataType => Model.DataType;

        /// <summary>Friendly type name.</summary>
        public string TypeName => Model.DataType.DisplayName;

        /// <summary>Optional tooltip.</summary>
        public string? Tooltip => Model.Tooltip;

        /// <summary>Index in the node's input or output list.</summary>
        public int Index { get; internal set; }

        /// <summary>Brush used for this port's dot and wires.</summary>
        public IBrush Brush => NodeEditorTheme.GetPortBrush(Kind);

        /// <summary>Whether this port currently participates in a connection.</summary>
        public bool IsConnected => Node.Editor.IsPortConnected(Model);

        /// <summary>Whether the pointer is hovering this port.</summary>
        public bool IsHovered { get; set; }

        /// <summary>Whether the current connection drag would accept this port.</summary>
        public bool IsDropTarget { get; set; }

        /// <summary>Graph-space anchor of the port.</summary>
        public Point Anchor => Node.GetPortAnchor(this);

        /// <summary>
        /// True when the default value can be edited directly inside the node row.
        /// </summary>
        public bool SupportsInlineEditing => IsInput && !IsConnected &&
            Kind is NodeValueKind.Float or NodeValueKind.Int or NodeValueKind.Bool;

        /// <summary>True when the port can display a colour swatch inline.</summary>
        public bool SupportsColorSwatch => IsInput && !IsConnected && Kind == NodeValueKind.Color;

        /// <summary>Refresh computed connection state after graph edits.</summary>
        public void NotifyConnectionChanged()
        {
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(SupportsInlineEditing));
            this.RaisePropertyChanged(nameof(SupportsColorSwatch));
            Node.InvalidateVisual();
        }
    }

    /// <summary>
    /// Editor wrapper around a <see cref="NodeConnection"/>.
    /// </summary>
    public sealed class ConnectionViewModel : ReactiveObject
    {
        private bool isSelected;

        internal ConnectionViewModel(NodeEditorWindowViewModel editor, NodeConnection connection,
            NodePortViewModel output, NodePortViewModel input)
        {
            Editor = editor;
            Model = connection;
            Output = output;
            Input = input;
        }

        /// <summary>Owning editor.</summary>
        public NodeEditorWindowViewModel Editor { get; }

        /// <summary>Model connection.</summary>
        public NodeConnection Model { get; }

        /// <summary>Upstream output port.</summary>
        public NodePortViewModel Output { get; }

        /// <summary>Downstream input port.</summary>
        public NodePortViewModel Input { get; }

        /// <summary>Graph-space start point of the wire.</summary>
        public Point Start => Output.Anchor;

        /// <summary>Graph-space end point of the wire.</summary>
        public Point End => Input.Anchor;

        /// <summary>Whether this wire is selected.</summary>
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value) return;
                isSelected = value;
                this.RaisePropertyChanged();
                Editor.InvalidateCanvas();
            }
        }

        /// <inheritdoc />
        public override string ToString() => Model.ToString();
    }
}
