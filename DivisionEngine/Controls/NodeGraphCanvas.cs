//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DivisionEngine.Editor.ViewModels;
using DivisionEngine.Nodes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DivisionEngine.Editor;

/// <summary>
/// Context information for a right-click on the graph canvas.
/// </summary>
public sealed class NodeCanvasContextRequestedEventArgs : EventArgs
{
    internal NodeCanvasContextRequestedEventArgs(Point screenPoint, Point worldPoint,
        NodeViewModel? node, NodePortViewModel? port, ConnectionViewModel? connection)
    {
        ScreenPoint = screenPoint;
        WorldPoint = worldPoint;
        Node = node;
        Port = port;
        Connection = connection;
    }

    /// <summary>Right-click position in canvas coordinates.</summary>
    public Point ScreenPoint { get; }

    /// <summary>Right-click position in graph coordinates.</summary>
    public Point WorldPoint { get; }

    /// <summary>Node under the pointer, if any.</summary>
    public NodeViewModel? Node { get; }

    /// <summary>Port under the pointer, if any.</summary>
    public NodePortViewModel? Port { get; }

    /// <summary>Connection under the pointer, if any.</summary>
    public ConnectionViewModel? Connection { get; }
}

/// <summary>
/// Custom-drawn graph surface used by <c>NodeEditorWindow</c>. Draws the grid, wires,
/// nodes and ports, and implements the interaction model: select, marquee, drag nodes,
/// drag wires between ports, edit inline values, pan and zoom.
/// </summary>
public sealed class NodeGraphCanvas : Control
{
    private enum DragMode
    {
        None,
        Nodes,
        Pan,
        Marquee,
        Connect,
        PortValue,
        PropertyValue,
    }

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);

    private NodeEditorWindowViewModel? editor;

    private DragMode dragMode;
    private Point dragStartScreen;
    private Point dragStartWorld;
    private bool dragMoved;
    private readonly Dictionary<NodeViewModel, Point> nodeDragOrigins = [];

    private Point panStartScreen;
    private double panStartX;
    private double panStartY;

    private Point marqueeStartWorld;
    private Point marqueeCurrentWorld;
    private bool marqueeAdditive;

    private NodePortViewModel? connectSource;
    private bool connectSourceIsOutput;
    private bool detachedDuringDrag;
    private NodePortViewModel? hoverPort;
    private NodePortViewModel? dropPort;

    private NodeViewModel? hoverNode;

    private NodePortViewModel? valueDragPort;
    private NodePropertyViewModel? valueDragProperty;
    private object? valueDragStartValue;
    private double valueDragStartScreenX;

    private Point pointerScreen;
    private Point pointerWorld;
    private KeyModifiers modifiers;

    /// <summary>Raised when the user right-clicks the graph.</summary>
    public event EventHandler<NodeCanvasContextRequestedEventArgs>? GraphContextRequested;

    /// <summary>Raised when the user asks to save the graph.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised when the user asks to open a graph.</summary>
    public event EventHandler? LoadRequested;

    /// <summary>Creates a graph canvas.</summary>
    public NodeGraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = ArrowCursor;
        SizeChanged += (_, _) => editor?.SetViewport(Bounds.Width, Bounds.Height);
        Loaded += (_, _) => editor?.SetViewport(Bounds.Width, Bounds.Height);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachEditor(DataContext as NodeEditorWindowViewModel);
    }

    private void AttachEditor(NodeEditorWindowViewModel? viewModel)
    {
        if (ReferenceEquals(editor, viewModel)) return;
        if (editor is not null)
        {
            editor.CanvasInvalidated -= OnCanvasInvalidated;
            editor.GraphStructureChanged -= OnGraphStructureChanged;
        }

        editor = viewModel;
        if (editor is not null)
        {
            editor.CanvasInvalidated += OnCanvasInvalidated;
            editor.GraphStructureChanged += OnGraphStructureChanged;
            editor.SetViewport(Bounds.Width, Bounds.Height);
        }
        InvalidateVisual();
    }

    private void OnCanvasInvalidated(object? sender, EventArgs e) => InvalidateVisual();

    private void OnGraphStructureChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect viewport = new(Bounds.Size);
        context.FillRectangle(NodeEditorTheme.CanvasBackground, viewport);

        NodeEditorWindowViewModel? vm = editor;
        if (vm is null) return;

        if (vm.ShowGrid) DrawGrid(context, vm, viewport);
        DrawConnections(context, vm);
        DrawPendingConnection(context, vm);
        DrawNodes(context, vm);
        DrawMarquee(context, vm);

        if (vm.Nodes.Count == 0) DrawEmptyState(context, vm, viewport);
    }

    private void DrawGrid(DrawingContext context, NodeEditorWindowViewModel vm, Rect viewport)
    {
        double zoom = vm.Zoom;
        double minorWorld = zoom < 0.45 ? 120 : 24;
        double majorWorld = minorWorld * 5;
        IPen minorPen = new Pen(NodeEditorTheme.GridDot, 1);
        IPen majorPen = new Pen(NodeEditorTheme.GridDotMajor, 1);

        double minorPixels = minorWorld * zoom;
        if (minorPixels >= 9)
            DrawGridLines(context, viewport, minorPixels, vm.PanX, vm.PanY, minorPen);
        DrawGridLines(context, viewport, majorWorld * zoom, vm.PanX, vm.PanY, majorPen);
    }

    private static void DrawGridLines(DrawingContext context, Rect viewport, double step,
        double panX, double panY, IPen pen)
    {
        if (step < 4) return;
        double offsetX = ((panX % step) + step) % step;
        double offsetY = ((panY % step) + step) % step;
        for (double x = offsetX; x < viewport.Width; x += step)
            context.DrawLine(pen, new Point(Math.Floor(x) + 0.5, 0), new Point(Math.Floor(x) + 0.5, viewport.Height));
        for (double y = offsetY; y < viewport.Height; y += step)
            context.DrawLine(pen, new Point(0, Math.Floor(y) + 0.5), new Point(viewport.Width, Math.Floor(y) + 0.5));
    }

    private void DrawConnections(DrawingContext context, NodeEditorWindowViewModel vm)
    {
        double zoom = vm.Zoom;
        foreach (ConnectionViewModel connection in vm.Connections)
        {
            Point start = vm.WorldToScreen(connection.Start);
            Point end = vm.WorldToScreen(connection.End);
            StreamGeometry geometry = CreateWire(start, end);
            IBrush brush = connection.IsSelected ? NodeEditorTheme.Selection : connection.Output.Brush;
            double thickness = connection.IsSelected ? 3.1 : Math.Max(1.5, 2.1 * zoom);
            context.DrawGeometry(null, new Pen(brush, thickness), geometry);
        }
    }

    private void DrawPendingConnection(DrawingContext context, NodeEditorWindowViewModel vm)
    {
        if (connectSource is null || dragMode != DragMode.Connect) return;

        Point sourceAnchor = vm.WorldToScreen(connectSource.Anchor);
        Point start = connectSourceIsOutput ? sourceAnchor : pointerScreen;
        Point end = connectSourceIsOutput ? pointerScreen : sourceAnchor;
        if (dropPort is not null) end = vm.WorldToScreen(dropPort.Anchor);

        IBrush brush = dropPort is not null
            ? NodeEditorTheme.ConnectionValid
            : connectSource.Brush;
        if (dropPort is null && hoverPort is not null && hoverPort.IsInput != connectSource.IsInput)
            brush = NodeEditorTheme.ConnectionInvalid;

        StreamGeometry geometry = CreateWire(start, end);
        context.DrawGeometry(null, new Pen(brush, 2.2, new DashStyle([6, 4], 0)), geometry);
        context.DrawEllipse(brush, null, end, 4.2, 4.2);
    }

    private static StreamGeometry CreateWire(Point start, Point end)
    {
        double control = Math.Max(40, Math.Abs(end.X - start.X) * 0.45);
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false);
            context.CubicBezierTo(
                new Point(start.X + control, start.Y),
                new Point(end.X - control, end.Y),
                end);
            context.EndFigure(isClosed: false);
        }
        return geometry;
    }

    private void DrawNodes(DrawingContext context, NodeEditorWindowViewModel vm)
    {
        Rect viewport = new(Bounds.Size);
        foreach (NodeViewModel node in GetDrawOrder(vm))
        {
            Rect bounds = vm.WorldToScreen(node.Bounds);
            if (!bounds.Intersects(viewport.Inflate(120))) continue;
            DrawNode(context, vm, node, bounds);
        }
    }

    private IEnumerable<NodeViewModel> GetDrawOrder(NodeEditorWindowViewModel vm)
    {
        foreach (NodeViewModel node in vm.Nodes)
            if (!node.IsSelected) yield return node;
        foreach (NodeViewModel node in vm.Nodes)
            if (node.IsSelected) yield return node;
    }

    private void DrawNode(DrawingContext context, NodeEditorWindowViewModel vm, NodeViewModel node, Rect bounds)
    {
        double zoom = vm.Zoom;
        double radius = Math.Clamp(6 * zoom, 2, 12);
        bool showText = zoom >= 0.42;

        Rect shadow = new(bounds.X + 2 * zoom, bounds.Y + 4 * zoom, bounds.Width, bounds.Height);
        context.DrawRectangle(NodeEditorTheme.NodeShadow, null, shadow, radius, radius);
        context.DrawRectangle(NodeEditorTheme.NodeBody, null, bounds, radius, radius);

        double headerHeight = node.HeaderHeight * zoom;
        Rect header = new(bounds.X, bounds.Y, bounds.Width, headerHeight);
        Rect accent = new(bounds.X, bounds.Y, Math.Max(2, 4 * zoom), headerHeight);

        using (context.PushClip(new RoundedRect(bounds, radius, radius)))
        {
            context.FillRectangle(NodeEditorTheme.NodeHeader, header);
            context.FillRectangle(node.AccentBrush, accent);

            if (showText)
            {
                FormattedText title = Format(node.Name, 12.2, NodeEditorTheme.TextPrimary,
                    Math.Max(20, bounds.Width - 26 * zoom));
                context.DrawText(title, new Point(bounds.X + 12 * zoom,
                    bounds.Y + (headerHeight - title.Height) / 2 - zoom));
            }

            Rect content = vm.WorldToScreen(new Rect(node.X, node.Y + node.ContentTop,
                node.Width, node.ContentHeight));
            if (content.Height > 2 && content.Width > 2)
            {
                using (context.PushClip(new RoundedRect(content, 3, 3)))
                    node.DrawContent(new NodeContentDrawContext(context, content, zoom, vm, node));
            }
        }

        IBrush borderBrush = node.IsSelected
            ? NodeEditorTheme.NodeBorderSelected
            : node.IsHovered
                ? NodeEditorTheme.NodeBorderHover
                : NodeEditorTheme.NodeBorder;
        double borderThickness = node.IsSelected ? 2.1 : 1.2;
        context.DrawRectangle(null, new Pen(borderBrush, borderThickness), bounds, radius, radius);

        if (node.IsSelected)
            context.DrawRectangle(null, new Pen(NodeEditorTheme.Selection, 1), bounds.Inflate(3), radius + 2, radius + 2);

        DrawPorts(context, vm, node, bounds, showText);
    }

    private void DrawPorts(DrawingContext context, NodeEditorWindowViewModel vm, NodeViewModel node,
        Rect bounds, bool showText)
    {
        double zoom = vm.Zoom;
        double dotRadius = Math.Clamp(4.4 * zoom, 2.4, 7.5);

        foreach (NodePortViewModel port in node.Inputs)
        {
            Point anchor = vm.WorldToScreen(node.GetPortAnchor(port));
            Rect? field = null;
            if (!port.IsConnected && node.ContentTop >= 0)
                field = vm.WorldToScreen(node.GetInlineFieldRect(port));

            if (showText)
            {
                double labelX = bounds.X + 12 * zoom;
                double maxWidth = field is Rect f ? Math.Max(12, f.X - labelX - 6 * zoom) : Math.Max(12, bounds.Width - 24 * zoom);
                FormattedText label = Format(port.Name, 10, NodeEditorTheme.TextSecondary, maxWidth);
                context.DrawText(label, new Point(labelX,
                    anchor.Y - label.Height / 2));
            }

            if (field is Rect fieldRect && fieldRect.Width > 8)
                DrawInlineField(context, vm, port, fieldRect, zoom, showText);

            DrawPortDot(context, port, anchor, dotRadius);
        }

        foreach (NodePortViewModel port in node.Outputs)
        {
            Point anchor = vm.WorldToScreen(node.GetPortAnchor(port));
            if (showText)
            {
                FormattedText label = Format(port.Name, 10, NodeEditorTheme.TextSecondary,
                    Math.Max(12, 44 * zoom));
                context.DrawText(label, new Point(anchor.X - 12 * zoom - label.Width,
                    anchor.Y - label.Height / 2));
            }
            DrawPortDot(context, port, anchor, dotRadius);
        }
    }

    private void DrawInlineField(DrawingContext context, NodeEditorWindowViewModel vm,
        NodePortViewModel port, Rect field, double zoom, bool showText)
    {
        context.DrawRectangle(NodeEditorTheme.FieldBackground, null, field, 3, 3);
        context.DrawRectangle(null, new Pen(NodeEditorTheme.FieldBorder, Math.Max(0.6, zoom)), field, 3, 3);

        object? value = port.Model.DefaultValue;
        if (port.Kind == NodeValueKind.Bool)
        {
            double size = Math.Min(field.Height - 3 * zoom, 13 * zoom);
            Rect check = new(field.X + 5 * zoom, field.Y + (field.Height - size) / 2, size, size);
            context.DrawRectangle(value is true ? NodeEditorTheme.Selection : NodeEditorTheme.FieldBackground,
                null, check, 2, 2);
            context.DrawRectangle(null, new Pen(NodeEditorTheme.FieldBorder, Math.Max(0.6, zoom)), check, 2, 2);
            return;
        }

        if (port.Kind == NodeValueKind.Color && value is float4 color)
        {
            Rect swatch = new(field.X + 3 * zoom, field.Y + 3 * zoom,
                Math.Max(4, field.Width - 6 * zoom), field.Height - 6 * zoom);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(
                ToByte(color.w), ToByte(color.x), ToByte(color.y), ToByte(color.z))), null, swatch, 2, 2);
            return;
        }

        if (!showText) return;
        FormattedText text = Format(FormatPortValue(value), 9.5, NodeEditorTheme.TextPrimary,
            Math.Max(8, field.Width - 10 * zoom));
        context.DrawText(text, new Point(field.X + 5 * zoom, field.Y + (field.Height - text.Height) / 2 - zoom));
    }

    private static void DrawPortDot(DrawingContext context, NodePortViewModel port, Point anchor, double radius)
    {
        IBrush fill = port.IsConnected ? port.Brush : NodeEditorTheme.FieldBackground;
        IPen pen = new Pen(port.Brush, Math.Max(1.2, radius * 0.35));
        context.DrawEllipse(fill, pen, anchor, radius, radius);

        if (port.IsDropTarget)
            context.DrawEllipse(null, new Pen(NodeEditorTheme.ConnectionValid, 2.2), anchor, radius + 3.5, radius + 3.5);
        else if (port.IsHovered)
            context.DrawEllipse(null, new Pen(NodeEditorTheme.TextPrimary, 1.6), anchor, radius + 2.5, radius + 2.5);
    }

    private void DrawMarquee(DrawingContext context, NodeEditorWindowViewModel vm)
    {
        if (dragMode != DragMode.Marquee) return;
        Point a = vm.WorldToScreen(marqueeStartWorld);
        Point b = vm.WorldToScreen(marqueeCurrentWorld);
        Rect rect = new(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(36, 70, 110, 150)), rect);
        context.DrawRectangle(null, new Pen(NodeEditorTheme.Selection, 1.2, new DashStyle([5, 3], 0)), rect);
    }

    private void DrawEmptyState(DrawingContext context, NodeEditorWindowViewModel vm, Rect viewport)
    {
        FormattedText title = Format("Empty graph", 18, NodeEditorTheme.TextSecondary);
        FormattedText hint = Format("Right-click anywhere or press Space to add a node", 11, NodeEditorTheme.TextDim);
        double centerX = viewport.Width / 2;
        double centerY = viewport.Height / 2;
        context.DrawText(title, new Point(centerX - title.Width / 2, centerY - title.Height));
        context.DrawText(hint, new Point(centerX - hint.Width / 2, centerY + 4));
    }

    private static FormattedText Format(string text, double size, IBrush brush, double maxWidth = double.PositiveInfinity)
    {
        FormattedText formatted = new(
            text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            NodeEditorTheme.Typeface,
            Math.Max(1, size),
            brush);
        if (!double.IsInfinity(maxWidth) && maxWidth > 0) formatted.MaxTextWidth = maxWidth;
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        return formatted;
    }

    private static string FormatPortValue(object? value)
    {
        return value switch
        {
            null => "—",
            float f => f.ToString("0.###", CultureInfo.InvariantCulture),
            double d => d.ToString("0.###", CultureInfo.InvariantCulture),
            bool b => b ? "On" : "Off",
            int i => i.ToString(CultureInfo.InvariantCulture),
            float2 v => string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}", v.x, v.y),
            float3 v => string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}, {2:0.##}", v.x, v.y, v.z),
            float4 v => string.Format(CultureInfo.InvariantCulture, "{0:0.##}, {1:0.##}, {2:0.##}, {3:0.##}", v.x, v.y, v.z, v.w),
            Enum e => e.ToString(),
            _ => value.ToString() ?? "—",
        };
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);

    #region interaction

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (editor is null) return;

        pointerScreen = e.GetPosition(this);
        pointerWorld = editor.ScreenToWorld(pointerScreen);
        modifiers = e.KeyModifiers;
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        modifiers = e.KeyModifiers;

        if (properties.IsMiddleButtonPressed ||
            (properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            BeginPan(pointerScreen);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            RaiseContextMenu(pointerScreen, pointerWorld);
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;

        NodePortViewModel? port = HitTestPort(pointerScreen);
        if (port is not null)
        {
            if (port.IsInput && port.IsConnected)
            {
                if (editor.DetachInput(port, out NodePortViewModel? source) && source is not null)
                {
                    detachedDuringDrag = true;
                    BeginConnectionDrag(source, isOutput: true);
                }
            }
            else BeginConnectionDrag(port, isOutput: port.IsOutput);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (HitTestPortValue(pointerWorld, out NodePortViewModel? valuePort, out _) && valuePort is not null)
        {
            editor.SelectNode(valuePort.Node);
            if (valuePort.SupportsInlineEditing)
            {
                BeginValueDrag(valuePort, null, pointerScreen);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            editor.ShowInspector = true;
            e.Handled = true;
            return;
        }

        if (HitTestPropertyValue(pointerWorld, out NodePropertyViewModel? property) && property is not null)
        {
            editor.SelectNode(property.Node);
            if (property.IsNumeric || property.IsBoolean)
            {
                BeginValueDrag(null, property, pointerScreen);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            editor.ShowInspector = true;
            e.Handled = true;
            return;
        }

        NodeViewModel? node = HitTestNode(pointerWorld);
        if (node is not null)
        {
            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (!node.IsSelected)
                editor.SelectNode(node, additive);
            else if (additive)
                editor.SelectNode(node, additive: true, toggle: true);

            if (node.IsSelected)
            {
                BeginNodeDrag(pointerScreen, pointerWorld);
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return;
        }

        ConnectionViewModel? connection = HitTestConnection(pointerScreen);
        if (connection is not null)
        {
            editor.SelectConnection(connection);
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) editor.ClearSelection();
        BeginMarquee(pointerWorld, e.KeyModifiers.HasFlag(KeyModifiers.Control));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (editor is null) return;

        pointerScreen = e.GetPosition(this);
        pointerWorld = editor.ScreenToWorld(pointerScreen);
        modifiers = e.KeyModifiers;

        switch (dragMode)
        {
            case DragMode.Nodes:
                UpdateNodeDrag();
                break;
            case DragMode.Pan:
                editor.PanX = panStartX + (pointerScreen.X - panStartScreen.X);
                editor.PanY = panStartY + (pointerScreen.Y - panStartScreen.Y);
                break;
            case DragMode.Marquee:
                marqueeCurrentWorld = pointerWorld;
                InvalidateVisual();
                break;
            case DragMode.Connect:
                UpdateConnectionDrag();
                break;
            case DragMode.PortValue:
            case DragMode.PropertyValue:
                UpdateValueDrag();
                break;
            default:
                UpdateHover();
                break;
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (editor is null) return;

        pointerScreen = e.GetPosition(this);
        pointerWorld = editor.ScreenToWorld(pointerScreen);

        switch (dragMode)
        {
            case DragMode.Nodes:
                editor.EndChange();
                if (dragMoved) editor.StatusText = "Moved selection.";
                break;
            case DragMode.Connect:
                FinishConnectionDrag();
                break;
            case DragMode.Marquee:
                FinishMarquee();
                break;
            case DragMode.PortValue:
            case DragMode.PropertyValue:
                FinishValueDrag();
                break;
        }

        ResetInteraction();
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (editor is null) return;
        Point position = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        editor.ZoomAt(position, factor);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (dragMode is DragMode.PortValue or DragMode.PropertyValue or DragMode.Nodes) editor?.EndChange();
        ResetInteraction();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHoverNode(null);
        SetHoverPort(null);
        Cursor = ArrowCursor;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (editor is null) return;
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                editor.DeleteSelection();
                e.Handled = true;
                break;
            case Key.Space when !control:
                editor.OpenNodeSearch(pointerWorld.X, pointerWorld.Y);
                e.Handled = true;
                break;
            case Key.Escape:
                if (editor.IsNodeSearchOpen) editor.CloseNodeSearch();
                else if (dragMode != DragMode.None)
                {
                    if (dragMode is DragMode.PortValue or DragMode.PropertyValue or DragMode.Nodes) editor.EndChange();
                    ResetInteraction();
                }
                else editor.ClearSelection();
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.F when !control:
                editor.FrameSelection();
                e.Handled = true;
                break;
            case Key.Home:
                editor.FrameAll();
                e.Handled = true;
                break;
            case Key.A when control:
                editor.SelectNodes(editor.Nodes, additive: false);
                e.Handled = true;
                break;
            case Key.D when control:
                editor.DuplicateSelection();
                e.Handled = true;
                break;
            case Key.Z when control:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) editor.Redo();
                else editor.Undo();
                e.Handled = true;
                break;
            case Key.Y when control:
                editor.Redo();
                e.Handled = true;
                break;
            case Key.S when control:
                SaveRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.O when control:
                LoadRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void BeginPan(Point screen)
    {
        dragMode = DragMode.Pan;
        panStartScreen = screen;
        panStartX = editor?.PanX ?? 0;
        panStartY = editor?.PanY ?? 0;
        Cursor = PanCursor;
    }

    private void BeginNodeDrag(Point screen, Point world)
    {
        dragMode = DragMode.Nodes;
        dragStartScreen = screen;
        dragStartWorld = world;
        dragMoved = false;
        nodeDragOrigins.Clear();
        if (editor is null) return;

        foreach (NodeViewModel node in editor.SelectedNodes)
            nodeDragOrigins[node] = new Point(node.X, node.Y);
        editor.BeginChange("Move Nodes");
    }

    private void UpdateNodeDrag()
    {
        if (editor is null) return;
        double dx = pointerWorld.X - dragStartWorld.X;
        double dy = pointerWorld.Y - dragStartWorld.Y;
        if (!dragMoved && (Math.Abs(pointerScreen.X - dragStartScreen.X) > 2 ||
                           Math.Abs(pointerScreen.Y - dragStartScreen.Y) > 2))
            dragMoved = true;
        if (!dragMoved) return;

        foreach ((NodeViewModel node, Point origin) in nodeDragOrigins)
        {
            double x = origin.X + dx;
            double y = origin.Y + dy;
            if (editor.SnapToGrid)
            {
                const double grid = 20;
                x = Math.Round(x / grid) * grid;
                y = Math.Round(y / grid) * grid;
            }
            node.X = x;
            node.Y = y;
        }

        editor.StatusText = "Drag to move · release to commit · Ctrl+Z to undo";
        InvalidateVisual();
    }

    private void BeginMarquee(Point world, bool additive)
    {
        dragMode = DragMode.Marquee;
        marqueeStartWorld = world;
        marqueeCurrentWorld = world;
        marqueeAdditive = additive;
        InvalidateVisual();
    }

    private void FinishMarquee()
    {
        if (editor is null) return;
        Rect world = new(
            Math.Min(marqueeStartWorld.X, marqueeCurrentWorld.X),
            Math.Min(marqueeStartWorld.Y, marqueeCurrentWorld.Y),
            Math.Abs(marqueeCurrentWorld.X - marqueeStartWorld.X),
            Math.Abs(marqueeCurrentWorld.Y - marqueeStartWorld.Y));

        if (world.Width < 2 && world.Height < 2) return;
        List<NodeViewModel> hits = editor.Nodes.Where(n => n.Bounds.Intersects(world)).ToList();
        editor.SelectNodes(hits, marqueeAdditive);
        editor.StatusText = hits.Count == 0
            ? "Selection cleared."
            : hits.Count == 1 ? $"Selected {hits[0].Name}." : $"Selected {hits.Count} nodes.";
    }

    private void BeginConnectionDrag(NodePortViewModel port, bool isOutput)
    {
        dragMode = DragMode.Connect;
        connectSource = port;
        connectSourceIsOutput = isOutput;
        SetHoverPort(port);
        UpdateConnectionDrag();
    }

    private void UpdateConnectionDrag()
    {
        if (editor is null || connectSource is null) return;
        NodePortViewModel? candidate = HitTestPort(pointerScreen);
        if (candidate is not null && ReferenceEquals(candidate, connectSource)) candidate = null;
        if (candidate is not null && candidate.IsInput == connectSource.IsInput) candidate = null;

        SetHoverPort(candidate ?? connectSource);
        bool valid = false;
        if (candidate is not null)
        {
            NodePortViewModel output = connectSourceIsOutput ? connectSource : candidate;
            NodePortViewModel input = connectSourceIsOutput ? candidate : connectSource;
            valid = editor.Graph.CanConnect(output.Model, input.Model, out _);
        }

        SetDropPort(valid ? candidate : null);
        InvalidateVisual();
    }

    private void FinishConnectionDrag()
    {
        if (editor is null || connectSource is null) return;
        NodePortViewModel? target = dropPort ?? HitTestPort(pointerScreen);
        if (target is not null && !ReferenceEquals(target, connectSource) && target.IsInput != connectSource.IsInput)
        {
            NodePortViewModel output = connectSourceIsOutput ? connectSource : target;
            NodePortViewModel input = connectSourceIsOutput ? target : connectSource;
            editor.ConnectPorts(output, input);
            editor.StatusText = $"Connected {output.Node.Name}.{output.Name} → {input.Node.Name}.{input.Name}";
        }
        else if (target is null)
        {
            editor.StatusText = detachedDuringDrag
                ? "Disconnected input."
                : "Connection cancelled. Drop a wire on a compatible port.";
        }

        SetDropPort(null);
        SetHoverPort(null);
    }

    private void BeginValueDrag(NodePortViewModel? port, NodePropertyViewModel? property, Point screen)
    {
        if (editor is null) return;
        dragMode = port is not null ? DragMode.PortValue : DragMode.PropertyValue;
        valueDragPort = port;
        valueDragProperty = property;
        valueDragStartValue = port?.Model.DefaultValue ?? property?.Value;
        valueDragStartScreenX = screen.X;
        dragMoved = false;
        string label = port is not null
            ? $"Edit {port.Node.Name}.{port.Name}"
            : $"Edit {property?.Node.Name}.{property?.DisplayName}";
        editor.BeginChange(label);
        Cursor = ResizeCursor;
    }

    private void UpdateValueDrag()
    {
        if (editor is null) return;
        double dx = pointerScreen.X - valueDragStartScreenX;
        if (!dragMoved && Math.Abs(dx) > 2) dragMoved = true;
        if (!dragMoved) return;

        bool fine = modifiers.HasFlag(KeyModifiers.Shift);
        bool coarse = modifiers.HasFlag(KeyModifiers.Control);
        double scale = fine ? 0.15 : coarse ? 5.0 : 1.0;

        object? start = valueDragStartValue;
        if (valueDragPort is not null)
        {
            double step = valueDragPort.Model.Step > 0
                ? valueDragPort.Model.Step
                : valueDragPort.Kind == NodeValueKind.Int ? 1 : 0.01;
            double baseValue = start is int intValue ? intValue : Convert.ToDouble(start ?? 0, CultureInfo.InvariantCulture);
            double newValue = valueDragPort.Model.ClampNumeric(baseValue + dx / 4.0 * step * scale);
            object value = valueDragPort.Kind == NodeValueKind.Int
                ? (int)Math.Round(newValue)
                : (float)newValue;
            editor.SetPortDefault(valueDragPort, value);
        }
        else if (valueDragProperty is not null)
        {
            double step = valueDragProperty.Step ?? (valueDragProperty.ValueType == typeof(int) ? 1 : 0.01);
            double baseValue = start is int i ? i : Convert.ToDouble(start ?? 0, CultureInfo.InvariantCulture);
            double newValue = baseValue + dx / 4.0 * step * scale;
            if (valueDragProperty.Min is double min) newValue = Math.Max(min, newValue);
            if (valueDragProperty.Max is double max) newValue = Math.Min(max, newValue);
            object value = valueDragProperty.ValueType == typeof(int)
                ? (int)Math.Round(newValue)
                : valueDragProperty.ValueType == typeof(double) ? newValue : (float)newValue;
            editor.SetNodeProperty(valueDragProperty, value);
        }
    }

    private void FinishValueDrag()
    {
        if (editor is null) return;

        if (!dragMoved && valueDragProperty is not null && valueDragProperty.IsBoolean)
        {
            object? current = valueDragProperty.Value;
            editor.SetNodeProperty(valueDragProperty, current is true ? false : true);
        }
        else if (!dragMoved && valueDragPort is not null && valueDragPort.Kind == NodeValueKind.Bool)
        {
            bool current = valueDragPort.Model.DefaultValue is true;
            editor.SetPortDefault(valueDragPort, !current);
        }

        editor.EndChange();
    }

    private void ResetInteraction()
    {
        dragMode = DragMode.None;
        dragMoved = false;
        nodeDragOrigins.Clear();
        connectSource = null;
        connectSourceIsOutput = false;
        detachedDuringDrag = false;
        SetDropPort(null);
        valueDragPort = null;
        valueDragProperty = null;
        valueDragStartValue = null;
        Cursor = ArrowCursor;
    }

    private void UpdateHover()
    {
        NodePortViewModel? port = HitTestPort(pointerScreen);
        if (port is not null)
        {
            SetHoverPort(port);
            SetHoverNode(port.Node);
            Cursor = HandCursor;
            return;
        }

        if (HitTestPortValue(pointerWorld, out NodePortViewModel? valuePort, out _) &&
            valuePort is not null && valuePort.SupportsInlineEditing)
        {
            SetHoverPort(valuePort);
            SetHoverNode(valuePort.Node);
            Cursor = valuePort.Kind == NodeValueKind.Bool ? HandCursor : ResizeCursor;
            return;
        }

        if (HitTestPropertyValue(pointerWorld, out NodePropertyViewModel? property) && property is not null)
        {
            SetHoverNode(property.Node);
            Cursor = property.IsNumeric && !property.IsBoolean ? ResizeCursor : HandCursor;
            return;
        }

        NodeViewModel? node = HitTestNode(pointerWorld);
        SetHoverNode(node);
        SetHoverPort(null);
        Cursor = node is not null ? HandCursor : ArrowCursor;
    }

    private void SetHoverNode(NodeViewModel? node)
    {
        if (ReferenceEquals(hoverNode, node)) return;
        if (hoverNode is not null) hoverNode.IsHovered = false;
        hoverNode = node;
        if (hoverNode is not null) hoverNode.IsHovered = true;
        InvalidateVisual();
    }

    private void SetHoverPort(NodePortViewModel? port)
    {
        if (ReferenceEquals(hoverPort, port)) return;
        if (hoverPort is not null) hoverPort.IsHovered = false;
        hoverPort = port;
        if (hoverPort is not null) hoverPort.IsHovered = true;
        InvalidateVisual();
    }

    private void SetDropPort(NodePortViewModel? port)
    {
        if (ReferenceEquals(dropPort, port)) return;
        if (dropPort is not null) dropPort.IsDropTarget = false;
        dropPort = port;
        if (dropPort is not null) dropPort.IsDropTarget = true;
        InvalidateVisual();
    }

    private NodePortViewModel? HitTestPort(Point screen)
    {
        if (editor is null) return null;
        double radius = Math.Max(14, 14 * editor.Zoom);
        double bestDistanceSquared = radius * radius;
        NodePortViewModel? best = null;

        foreach (NodeViewModel node in GetDrawOrder(editor).Reverse())
        {
            foreach (NodePortViewModel port in node.Inputs.Concat(node.Outputs))
            {
                Point anchor = editor.WorldToScreen(node.GetPortAnchor(port));
                double dx = screen.X - anchor.X;
                double dy = screen.Y - anchor.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > bestDistanceSquared) continue;
                bestDistanceSquared = distanceSquared;
                best = port;
            }
        }
        return best;
    }

    private NodeViewModel? HitTestNode(Point world)
    {
        if (editor is null) return null;
        foreach (NodeViewModel node in GetDrawOrder(editor).Reverse())
        {
            if (node.Bounds.Contains(world)) return node;
        }
        return null;
    }

    private bool HitTestPortValue(Point world, out NodePortViewModel? port, out Rect rect)
    {
        port = null;
        rect = default;
        if (editor is null) return false;
        foreach (NodeViewModel node in GetDrawOrder(editor).Reverse())
        {
            foreach (NodePortViewModel input in node.Inputs)
            {
                if (input.IsConnected) continue;
                Rect field = node.GetInlineFieldRect(input).Inflate(2 / Math.Max(editor.Zoom, 0.2));
                if (field.Contains(world))
                {
                    port = input;
                    rect = field;
                    return true;
                }
            }
        }
        return false;
    }

    private bool HitTestPropertyValue(Point world, out NodePropertyViewModel? property)
    {
        property = null;
        if (editor is null) return false;
        foreach (NodeViewModel node in GetDrawOrder(editor).Reverse())
        {
            for (int i = 0; i < node.Properties.Count; i++)
            {
                Rect valueRect = node.GetPropertyValueRect(i).Inflate(2 / Math.Max(editor.Zoom, 0.2));
                if (valueRect.Contains(world))
                {
                    property = node.Properties[i];
                    return true;
                }
            }
        }
        return false;
    }

    private ConnectionViewModel? HitTestConnection(Point screen)
    {
        if (editor is null) return null;
        const double threshold = 7;
        ConnectionViewModel? best = null;
        double bestDistance = double.MaxValue;

        foreach (ConnectionViewModel connection in editor.Connections)
        {
            Point start = editor.WorldToScreen(connection.Start);
            Point end = editor.WorldToScreen(connection.End);
            double control = Math.Max(40, Math.Abs(end.X - start.X) * 0.45);
            Point c1 = new(start.X + control, start.Y);
            Point c2 = new(end.X - control, end.Y);

            Point previous = start;
            for (int step = 1; step <= 24; step++)
            {
                double t = step / 24.0;
                Point current = CubicBezier(start, c1, c2, end, t);
                double distance = DistanceToSegment(screen, previous, current);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = connection;
                }
                previous = current;
            }
        }

        return bestDistance <= threshold ? best : null;
    }

    private static Point CubicBezier(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1 - t;
        double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
        double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
        return new Point(x, y);
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-6) return Math.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));
        double t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
        double px = a.X + t * dx;
        double py = a.Y + t * dy;
        return Math.Sqrt((point.X - px) * (point.X - px) + (point.Y - py) * (point.Y - py));
    }

    private void RaiseContextMenu(Point screen, Point world)
    {
        if (editor is null) return;
        NodePortViewModel? port = HitTestPort(screen);
        NodeViewModel? node = port?.Node ?? HitTestNode(world);
        ConnectionViewModel? connection = node is null && port is null ? HitTestConnection(screen) : null;

        if (port is not null && !port.Node.IsSelected) editor.SelectNode(port.Node);
        else if (port is null && node is not null && !node.IsSelected) editor.SelectNode(node);
        else if (connection is not null) editor.SelectConnection(connection);

        GraphContextRequested?.Invoke(this,
            new NodeCanvasContextRequestedEventArgs(screen, world, node, port, connection));
    }

    #endregion
}
