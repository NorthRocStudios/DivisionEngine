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
using Material.Icons;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// Drawing context handed to custom node content visualisations. It exposes the
    /// screen-space rectangle of the node body (below the port rows), the current zoom
    /// and helpers for drawing text.
    /// </summary>
    public sealed class NodeContentDrawContext
    {
        internal NodeContentDrawContext(
            DrawingContext context,
            Rect bounds,
            double zoom,
            NodeEditorWindowViewModel editor,
            NodeViewModel node)
        {
            Context = context;
            Bounds = bounds;
            Zoom = zoom;
            Editor = editor;
            Node = node;
        }

        /// <summary>Avalonia drawing context.</summary>
        public DrawingContext Context { get; }

        /// <summary>Screen-space rectangle available to the node content.</summary>
        public Rect Bounds { get; }

        /// <summary>Current zoom factor. Use it to scale text, lines and previews.</summary>
        public double Zoom { get; }

        /// <summary>Owning editor.</summary>
        public NodeEditorWindowViewModel Editor { get; }

        /// <summary>Node being drawn.</summary>
        public NodeViewModel Node { get; }

        /// <summary>
        /// Creates a formatted text run using the editor fonts.
        /// </summary>
        /// <param name="text">Text to draw.</param>
        /// <param name="fontSize">Font size in graph units; scaled by zoom.</param>
        /// <param name="brush">Text brush.</param>
        /// <param name="maxWidth">Optional maximum width before trimming.</param>
        public FormattedText CreateText(string text, double fontSize, IBrush? brush, double maxWidth = double.PositiveInfinity)
        {
            FormattedText formatted = new(
                text ?? string.Empty,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                NodeEditorTheme.Typeface,
                Math.Max(1, fontSize * Zoom),
                brush ?? NodeEditorTheme.TextPrimary);
            if (!double.IsInfinity(maxWidth) && maxWidth > 0) formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
            return formatted;
        }

        /// <summary>
        /// Draws a text run at a screen position.
        /// </summary>
        /// <param name="text">Text to draw.</param>
        /// <param name="x">Screen X position.</param>
        /// <param name="y">Screen Y position.</param>
        /// <param name="fontSize">Font size in graph units.</param>
        /// <param name="brush">Text brush.</param>
        /// <param name="maxWidth">Optional maximum width before trimming.</param>
        public void DrawText(string text, double x, double y, double fontSize = 10.5, IBrush? brush = null, double maxWidth = double.PositiveInfinity)
        {
            FormattedText formatted = CreateText(text, fontSize, brush, maxWidth);
            Context.DrawText(formatted, new Point(x, y));
        }
    }

    /// <summary>
    /// Editor wrapper around a <see cref="Node"/>. Handles layout, selection state and
    /// content drawing. Derive from this class (and override
    /// <see cref="DrawContent"/>) to give a node type a custom in-node visualisation.
    /// </summary>
    public class NodeViewModel : ReactiveObject
    {
        private bool isSelected;
        private bool isHovered;

        /// <summary>
        /// Creates a node wrapper.
        /// </summary>
        /// <param name="model">Model node.</param>
        /// <param name="editor">Owning editor.</param>
        public NodeViewModel(Node model, NodeEditorWindowViewModel editor)
        {
            Model = model;
            Editor = editor;
            Registration = NodeTypeRegistry.GetRegistration(model.GetType());
            Category = Registration?.Category ?? "General";
            Icon = Enum.TryParse(Registration?.Icon, ignoreCase: true, out MaterialIconKind parsed)
                ? parsed
                : MaterialIconKind.Shape;
            MenuPath = Registration?.MenuPath ?? model.GetType().Name;
            Description = Registration?.Description;
            AccentBrush = NodeEditorTheme.GetCategoryBrush(Category);

            List<NodePortViewModel> inputs = [];
            foreach (NodePort port in model.Inputs) inputs.Add(new NodePortViewModel(this, port));
            for (int i = 0; i < inputs.Count; i++) inputs[i].Index = i;
            Inputs = inputs;

            List<NodePortViewModel> outputs = [];
            foreach (NodePort port in model.Outputs) outputs.Add(new NodePortViewModel(this, port));
            for (int i = 0; i < outputs.Count; i++) outputs[i].Index = i;
            Outputs = outputs;

            List<NodePropertyViewModel> properties = [];
            foreach (NodePropertyDescriptor descriptor in NodeReflection.GetProperties(model))
            {
                if (descriptor.HideInInspector) continue;
                properties.Add(new NodePropertyViewModel(this, descriptor));
            }
            Properties = properties;
        }

        /// <summary>Model node.</summary>
        public Node Model { get; }

        /// <summary>Owning editor.</summary>
        public NodeEditorWindowViewModel Editor { get; }

        /// <summary>Registry metadata, when the type is registered.</summary>
        public NodeTypeRegistration? Registration { get; }

        /// <summary>Input port wrappers.</summary>
        public IReadOnlyList<NodePortViewModel> Inputs { get; }

        /// <summary>Output port wrappers.</summary>
        public IReadOnlyList<NodePortViewModel> Outputs { get; }

        /// <summary>Quick-edit properties declared with <see cref="NodePropertyAttribute"/>.</summary>
        public IReadOnlyList<NodePropertyViewModel> Properties { get; }

        /// <summary>Display name.</summary>
        public string Name
        {
            get => Model.Name;
            set
            {
                if (string.Equals(Model.Name, value, StringComparison.Ordinal)) return;
                Model.Name = string.IsNullOrWhiteSpace(value) ? "Node" : value.Trim();
                this.RaisePropertyChanged();
                Editor.InvalidateCanvas();
            }
        }

        /// <summary>Menu path of the node type.</summary>
        public string MenuPath { get; }

        /// <summary>Top level category.</summary>
        public string Category { get; }

        /// <summary>Optional node description.</summary>
        public string? Description { get; }

        /// <summary>Material icon for the node type.</summary>
        public MaterialIconKind Icon { get; }

        /// <summary>Category accent brush.</summary>
        public IBrush AccentBrush { get; }

        /// <summary>Graph-space X position.</summary>
        public double X
        {
            get => Model.X;
            set
            {
                if (Math.Abs(Model.X - value) < 1e-6) return;
                Model.X = value;
                this.RaisePropertyChanged();
                Editor.InvalidateCanvas();
            }
        }

        /// <summary>Graph-space Y position.</summary>
        public double Y
        {
            get => Model.Y;
            set
            {
                if (Math.Abs(Model.Y - value) < 1e-6) return;
                Model.Y = value;
                this.RaisePropertyChanged();
                Editor.InvalidateCanvas();
            }
        }

        /// <summary>Whether the node is selected.</summary>
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

        /// <summary>Whether the pointer is over the node.</summary>
        public bool IsHovered
        {
            get => isHovered;
            set
            {
                if (isHovered == value) return;
                isHovered = value;
                this.RaisePropertyChanged();
                Editor.InvalidateCanvas();
            }
        }

        /// <summary>Width of the node in graph units.</summary>
        public virtual double Width => NodeEditorTheme.NodeWidth;

        /// <summary>Height of the header in graph units.</summary>
        public double HeaderHeight => NodeEditorTheme.HeaderHeight;

        /// <summary>Height of one port row in graph units.</summary>
        public double PortRowHeight => NodeEditorTheme.PortRowHeight;

        /// <summary>Height of the port block.</summary>
        public double PortAreaHeight => Math.Max(Inputs.Count, Outputs.Count) * PortRowHeight;

        /// <summary>Offset of the content area from the node origin.</summary>
        public double ContentTop => HeaderHeight + PortAreaHeight;

        /// <summary>Height of the content area. Override in custom node views.</summary>
        public virtual double ContentHeight => Properties.Count == 0 ? 0 : Properties.Count * NodeEditorTheme.PropertyRowHeight + 6;

        /// <summary>Total node height in graph units.</summary>
        public double Height => ContentTop + ContentHeight + NodeEditorTheme.ContentBottomPadding;

        /// <summary>Graph-space bounds.</summary>
        public Rect Bounds => new(X, Y, Width, Height);

        /// <summary>True when the node has no input or output ports.</summary>
        public bool IsSink => Outputs.Count == 0;

        /// <summary>True when the node produces no outputs but accepts inputs.</summary>
        public bool IsOutputNode => Outputs.Count == 0 && Inputs.Count > 0;

        /// <summary>
        /// Finds the wrapper for a model port.
        /// </summary>
        /// <param name="port">Model port.</param>
        public NodePortViewModel? GetPort(NodePort port)
        {
            foreach (NodePortViewModel view in Inputs)
                if (ReferenceEquals(view.Model, port)) return view;
            foreach (NodePortViewModel view in Outputs)
                if (ReferenceEquals(view.Model, port)) return view;
            return null;
        }

        /// <summary>
        /// Returns the graph-space anchor of a port.
        /// </summary>
        /// <param name="port">Port wrapper.</param>
        public Point GetPortAnchor(NodePortViewModel port)
        {
            double y = Y + HeaderHeight + port.Index * PortRowHeight + PortRowHeight / 2;
            double x = port.IsInput ? X : X + Width;
            return new Point(x, y);
        }

        /// <summary>
        /// Returns the graph-space rectangle of an inline default-value field.
        /// </summary>
        /// <param name="port">Input port wrapper.</param>
        public Rect GetInlineFieldRect(NodePortViewModel port)
        {
            double top = Y + HeaderHeight + port.Index * PortRowHeight + (PortRowHeight - 18) / 2;
            return new Rect(X + Width - 150, top, 88, 18);
        }

        /// <summary>
        /// Returns the graph-space row rectangle for a property.
        /// </summary>
        /// <param name="index">Property index.</param>
        public Rect GetPropertyRowRect(int index)
        {
            double top = Y + ContentTop + 3 + index * NodeEditorTheme.PropertyRowHeight;
            return new Rect(X + 8, top, Width - 16, NodeEditorTheme.PropertyRowHeight);
        }

        /// <summary>
        /// Returns the graph-space value box rectangle for a property.
        /// </summary>
        /// <param name="index">Property index.</param>
        public Rect GetPropertyValueRect(int index)
        {
            Rect row = GetPropertyRowRect(index);
            return new Rect(row.Right - 136, row.Y + 2, 128, 18);
        }

        /// <summary>
        /// Draws node content below the port rows. The default implementation draws
        /// <see cref="NodePropertyAttribute"/> rows; override it for previews.
        /// </summary>
        /// <param name="context">Drawing context.</param>
        public virtual void DrawContent(NodeContentDrawContext context)
        {
            for (int i = 0; i < Properties.Count; i++)
                DrawPropertyRow(context, i);
        }

        /// <summary>
        /// Draws one property row.
        /// </summary>
        /// <param name="context">Drawing context.</param>
        /// <param name="index">Property index.</param>
        protected void DrawPropertyRow(NodeContentDrawContext context, int index)
        {
            if (index < 0 || index >= Properties.Count) return;
            NodePropertyViewModel property = Properties[index];
            Rect row = Editor.WorldToScreen(GetPropertyRowRect(index));
            Rect field = Editor.WorldToScreen(GetPropertyValueRect(index));
            double zoom = context.Zoom;

            context.DrawText(property.DisplayName, row.X + 4 * zoom, row.Y + (row.Height - 10.5 * zoom) / 2 - 1,
                10.5, NodeEditorTheme.TextSecondary, Math.Max(10, row.Width - field.Width - 12 * zoom));

            if (property.IsBoolean)
            {
                double size = Math.Min(14 * zoom, field.Height - 2);
                Rect box = new(field.X + 4 * zoom, field.Y + (field.Height - size) / 2, size, size);
                context.Context.DrawRectangle(property.Value is true ? NodeEditorTheme.Selection : NodeEditorTheme.FieldBackground,
                    null, box, 2, 2);
                context.Context.DrawRectangle(null, new Pen(NodeEditorTheme.FieldBorder, Math.Max(0.6, zoom)), box, 2, 2);
                return;
            }

            context.Context.DrawRectangle(NodeEditorTheme.FieldBackground, null, field, 3, 3);
            context.Context.DrawRectangle(null, new Pen(NodeEditorTheme.FieldBorder, Math.Max(0.6, zoom)), field, 3, 3);

            if (property.IsColor)
            {
                Color color = property.ColorValue;
                Rect swatch = new(field.X + 3 * zoom, field.Y + 3 * zoom, Math.Max(4, 18 * zoom), field.Height - 6 * zoom);
                context.Context.DrawRectangle(new SolidColorBrush(color), null, swatch, 2, 2);
                context.DrawText(property.FormatValue(), swatch.Right + 4 * zoom,
                    field.Y + (field.Height - 10 * zoom) / 2 - 1, 10, NodeEditorTheme.TextPrimary,
                    Math.Max(10, field.Right - swatch.Right - 8 * zoom));
                return;
            }

            context.DrawText(property.FormatValue(), field.X + 6 * zoom,
                field.Y + (field.Height - 10 * zoom) / 2 - 1, 10, NodeEditorTheme.TextPrimary,
                Math.Max(10, field.Width - 12 * zoom));
        }

        /// <summary>Invalidates the owning canvas.</summary>
        public void InvalidateVisual() => Editor.InvalidateCanvas();

        /// <summary>Releases resources held by custom node views.</summary>
        public virtual void Dispose() { }
    }

    /// <summary>
    /// Editor wrapper around a <see cref="NodePropertyAttribute"/> member.
    /// </summary>
    public sealed class NodePropertyViewModel : ReactiveObject
    {
        internal NodePropertyViewModel(NodeViewModel node, NodePropertyDescriptor descriptor)
        {
            Node = node;
            Descriptor = descriptor;
        }

        /// <summary>Owning node.</summary>
        public NodeViewModel Node { get; }

        /// <summary>Reflected member metadata.</summary>
        public NodePropertyDescriptor Descriptor { get; }

        /// <summary>Display label.</summary>
        public string DisplayName => Descriptor.DisplayName;

        /// <summary>Member name.</summary>
        public string Name => Descriptor.Name;

        /// <summary>CLR type.</summary>
        public Type ValueType => Descriptor.ValueType;

        /// <summary>Optional tooltip.</summary>
        public string? Tooltip => Descriptor.Tooltip;

        /// <summary>Current value.</summary>
        public object? Value
        {
            get => Descriptor.GetValue(Node.Model);
            set => Node.Editor.SetNodeProperty(this, value);
        }

        /// <summary>Inclusive lower bound for numeric values.</summary>
        public double? Min => Descriptor.Min;

        /// <summary>Inclusive upper bound for numeric values.</summary>
        public double? Max => Descriptor.Max;

        /// <summary>Preferred increment for numeric values.</summary>
        public double? Step => Descriptor.Step;

        /// <summary>True for numeric values.</summary>
        public bool IsNumeric => Descriptor.IsNumeric;

        /// <summary>True for boolean values.</summary>
        public bool IsBoolean => ValueType == typeof(bool);

        /// <summary>True for string values.</summary>
        public bool IsString => ValueType == typeof(string);

        /// <summary>True for enum values.</summary>
        public bool IsEnum => ValueType.IsEnum;

        /// <summary>True for float4 values, drawn as colours.</summary>
        public bool IsColor => ValueType == typeof(float4);

        /// <summary>True for vector values.</summary>
        public bool IsVector => ValueType == typeof(float2) || ValueType == typeof(float3) || ValueType == typeof(float4);

        /// <summary>True when the property can be edited.</summary>
        public bool CanWrite => Descriptor.CanWrite;

        /// <summary>Current value converted to an Avalonia colour.</summary>
        public Color ColorValue
        {
            get
            {
                if (Value is float4 color)
                    return Color.FromArgb(
                        (byte)Math.Clamp(color.w * 255f, 0, 255),
                        (byte)Math.Clamp(color.x * 255f, 0, 255),
                        (byte)Math.Clamp(color.y * 255f, 0, 255),
                        (byte)Math.Clamp(color.z * 255f, 0, 255));
                return Colors.Magenta;
            }
        }

        /// <summary>Formatted value for inline display.</summary>
        public string FormatValue()
        {
            object? value = Value;
            return value switch
            {
                null => "—",
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                bool b => b ? "On" : "Off",
                float2 v => string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}", v.x, v.y),
                float3 v => string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}, {2:0.##}", v.x, v.y, v.z),
                float4 v => string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}, {2:0.##}, {3:0.##}", v.x, v.y, v.z, v.w),
                Enum e => e.ToString(),
                _ => value.ToString() ?? "—",
            };
        }

        /// <summary>Notifies listeners that the underlying value changed.</summary>
        public void NotifyValueChanged() => this.RaisePropertyChanged(nameof(Value));
    }
}
