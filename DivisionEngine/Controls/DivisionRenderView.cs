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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DivisionEngine.Editor.Systems;
using DivisionEngine.Input;
using System;
using System.Runtime.InteropServices;

namespace DivisionEngine.Editor.Controls;

/// <summary>
/// A custom control for displaying a render view in Avalonia UI.
/// </summary>
public class DivisionRenderView : Control
{
    private WriteableBitmap? bitmap;
    private int lastWidth = -1, lastHeight = -1;
    private bool isLookDragging = false;
    private PixelPoint dragCenterScreen;
    private PixelPoint dragStartScreen;
    private bool suppressNextMove = false;

    /// <summary>
    /// Create a new Division render view.
    /// </summary>
    public DivisionRenderView()
    {
        Focusable = true;
        ClipToBounds = true;

        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += (s, e) => App.UserInput?.SetKeyDown(EditorInput.AvaloniaToKeyCode(e.Key));
        KeyUp += (s, e) => App.UserInput?.SetKeyUp(EditorInput.AvaloniaToKeyCode(e.Key));
        PointerEntered += (s, e) => RenderWindowManagementSystem.RendererFocused = true;
        PointerExited += (s, e) => RenderWindowManagementSystem.RendererFocused = false;

        AttachedToVisualTree += (s, e) => Subscribe();
        DetachedFromVisualTree += (s, e) => Unsubscribe();
    }

    private void Subscribe()
    {
        if (App.Renderer != null) App.Renderer.FrameAvailable += OnFrameAvailable;
    }

    private void Unsubscribe()
    {
        if (App.Renderer != null) App.Renderer.FrameAvailable -= OnFrameAvailable;
    }

    private void OnFrameAvailable() => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);

    protected override Size ArrangeOverride(Size finalSize)
    {
        int w = Math.Max(1, (int)finalSize.Width);
        int h = Math.Max(1, (int)finalSize.Height);
        App.Renderer?.SetEmbeddedViewportSize(w, h);
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        int w = Math.Max(1, (int)Bounds.Width);
        int h = Math.Max(1, (int)Bounds.Height);

        if (bitmap == null || w != lastWidth || h != lastHeight)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            lastWidth = w; lastHeight = h;
        }

        using (var fb = bitmap.Lock())
        {
            if (App.Renderer?.TryCopyEmbeddedFrame(fb.Address, w, h) != true)
            {
                base.Render(context); // nothing new yet, skip draw this pass
                return;
            }
        }

        context.DrawImage(bitmap, new Rect(0, 0, w, h));
    }

    private static float2 LocalToRenderPixel(PointerEventArgs e, Visual v)
    {
        Point p = e.GetPosition(v);
        return new float2((float)p.X, (float)p.Y);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (isLookDragging)
        {
            if (suppressNextMove)
            {
                suppressNextMove = false;
                return;
            }

            PixelPoint curScreen = this.PointToScreen(e.GetPosition(this));
            int dx = curScreen.X - dragCenterScreen.X;
            int dy = curScreen.Y - dragCenterScreen.Y;

            if (dx != 0 || dy != 0)
            {
                float w = Math.Max(1f, (float)Bounds.Width);
                float h = Math.Max(1f, (float)Bounds.Height);
                App.UserInput?.AccumulateMouseUVDelta(new float2(dx / w, dy / h));

                suppressNextMove = true; // the warp below raises its own move - ignore it too
                WarpCursor(dragCenterScreen);
            }
            return; // don't fall through to the normal absolute-position path while dragging
        }

        float2 pos = LocalToRenderPixel(e, this);
        App.UserInput?.SetMousePosition(pos);
        App.UserInput?.SetRelativeMousePosition(pos, new float2((float)Bounds.Width, (float)Bounds.Height));
        if (!HandleInteractionSystem.IsDragging)
            App.Renderer?.UpdateHoveredHandle((int)pos.X, (int)pos.Y);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed) App.UserInput?.SetMouseKeyDown(MouseCode.Left);

        if (props.IsRightButtonPressed)
        {
            App.UserInput?.SetMouseKeyDown(MouseCode.Right);
            e.Pointer.Capture(this);

            dragStartScreen = this.PointToScreen(e.GetPosition(this));
            dragCenterScreen = this.PointToScreen(new Point(Bounds.Width / 2, Bounds.Height / 2));

            isLookDragging = true;
            suppressNextMove = true; // the warp below will itself raise a PointerMoved - ignore it
            WarpCursor(dragCenterScreen);

            Cursor = new Cursor(StandardCursorType.None);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        App.UserInput?.SetMouseKeyUp(MouseCode.Left);
        App.UserInput?.SetMouseKeyUp(MouseCode.Right);

        if (isLookDragging)
        {
            e.Pointer.Capture(null);
            isLookDragging = false;
            Cursor = Cursor.Default;

            // Restore the cursor to where the drag actually started, rather than
            // leaving it stranded at the recenter point - standard editor-camera UX,
            // and keeps InputSystem's absolute-position tracking sane for clicks after release
            WarpCursor(dragStartScreen);
            Point local = this.PointToClient(dragStartScreen);
            float2 restoredPos = new float2((float)local.X, (float)local.Y);
            App.UserInput?.SetMousePosition(restoredPos);
            App.UserInput?.SetRelativeMousePosition(restoredPos, new float2((float)Bounds.Width, (float)Bounds.Height));
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        App.UserInput?.SetMouseWheel(new float2((float)e.Delta.X, (float)e.Delta.Y));
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private static void WarpCursor(PixelPoint screenPos)
    {
        // Windows-only for now. On Linux/macOS this becomes a no-op, so drags will
        // still clamp at the monitor edge there until an X11/Cocoa warp is added.
        if (OperatingSystem.IsWindows()) SetCursorPos(screenPos.X, screenPos.Y);
    }
}
