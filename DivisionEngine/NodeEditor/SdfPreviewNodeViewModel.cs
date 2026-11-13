//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DivisionEngine.Nodes;
using DivisionEngine.Nodes.Sdf;
using System;

namespace DivisionEngine.Editor.ViewModels
{
    /// <summary>
    /// Node view that renders a small ray-marched preview of the signed distance field
    /// feeding an <see cref="SdfPreviewNode"/>. This is the reference implementation for
    /// custom node visualisations: derive from <see cref="NodeViewModel"/>, override
    /// <see cref="NodeViewModel.ContentHeight"/> and <see cref="NodeViewModel.DrawContent"/>.
    /// </summary>
    public sealed class SdfPreviewNodeViewModel : NodeViewModel
    {
        private const int PreviewWidth = 144;
        private const int PreviewHeight = 88;

        private WriteableBitmap? preview;
        private bool previewDirty = true;
        private string? previewError;
        private string previewDescription = "SDF";

        /// <summary>
        /// Creates the preview node view.
        /// </summary>
        /// <param name="model">Model node.</param>
        /// <param name="editor">Owning editor.</param>
        public SdfPreviewNodeViewModel(Node model, NodeEditorWindowViewModel editor) : base(model, editor)
        {
            Editor.NodeValueChanged += OnNodeValueChanged;
            Editor.GraphStructureChanged += OnGraphStructureChanged;
        }

        /// <inheritdoc />
        public override double ContentHeight => 156;

        private void OnNodeValueChanged(object? sender, NodeViewModel node) => previewDirty = true;

        private void OnGraphStructureChanged(object? sender, EventArgs e) => previewDirty = true;

        /// <inheritdoc />
        public override void DrawContent(NodeContentDrawContext context)
        {
            Rect bounds = context.Bounds;
            if (bounds.Width < 8 || bounds.Height < 8) return;

            if (previewDirty)
            {
                RebuildPreview();
                previewDirty = false;
            }

            Rect imageBounds = new(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
            if (preview is not null)
            {
                context.Context.DrawImage(preview, imageBounds);
            }
            else
            {
                context.Context.DrawRectangle(NodeEditorTheme.FieldBackground, null, imageBounds, 4, 4);
                string message = previewError ?? "Connect an SDF shape";
                FormattedText text = context.CreateText(message, 10.5, NodeEditorTheme.TextDim,
                    Math.Max(20, imageBounds.Width - 16));
                context.Context.DrawText(text, new Point(
                    imageBounds.X + (imageBounds.Width - text.Width) / 2,
                    imageBounds.Y + (imageBounds.Height - text.Height) / 2));
            }

            context.Context.DrawRectangle(null,
                new Pen(NodeEditorTheme.FieldBorder, Math.Max(0.6, context.Zoom)), imageBounds, 4, 4);

            double labelSize = 9.5 * context.Zoom;
            if (labelSize >= 6 && previewDescription.Length > 0)
            {
                FormattedText label = context.CreateText(previewDescription, 9.5,
                    NodeEditorTheme.TextSecondary, Math.Max(20, imageBounds.Width - 16));
                Rect labelBackground = new(imageBounds.X + 4 * context.Zoom,
                    imageBounds.Bottom - label.Height - 8 * context.Zoom,
                    Math.Min(imageBounds.Width - 8 * context.Zoom, label.Width + 8 * context.Zoom),
                    label.Height + 2 * context.Zoom);
                context.Context.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 16, 18, 21)),
                    null, labelBackground, 2, 2);
                context.Context.DrawText(label, new Point(labelBackground.X + 4 * context.Zoom,
                    labelBackground.Y + context.Zoom));
            }
        }

        private void RebuildPreview()
        {
            previewError = null;
            previewDescription = "SDF";
            NodePort? input = Model.FindPort("SDF", NodePortDirection.Input);
            string? error = null;
            if (input is null || !Editor.Graph.TryEvaluateInput(input, out object? value, out error))
            {
                previewError = error ?? "Connect an SDF shape";
                DisposePreview();
                return;
            }

            if (value is not SdfShape shape)
            {
                previewError = "No SDF shape connected";
                DisposePreview();
                return;
            }

            previewDescription = shape.Description;
            WriteableBitmap bitmap = new(
                new PixelSize(PreviewWidth, PreviewHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);

            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                unsafe
                {
                    byte* pixels = (byte*)buffer.Address;
                    int stride = buffer.RowBytes;
                    Render(shape, pixels, stride);
                }
            }

            DisposePreview();
            preview = bitmap;
        }

        private static unsafe void Render(SdfShape shape, byte* pixels, int stride)
        {
            float3 sun = new(-0.45f, 0.75f, -0.55f);
            float sunLength = MathF.Sqrt(sun.x * sun.x + sun.y * sun.y + sun.z * sun.z);
            sun = new float3(sun.x / sunLength, sun.y / sunLength, sun.z / sunLength);
            float4 baseColor = shape.Color;
            float aspect = PreviewWidth / (float)PreviewHeight;

            for (int y = 0; y < PreviewHeight; y++)
            {
                float v = ((y + 0.5f) / PreviewHeight * 2f - 1f) * 1.15f;
                for (int x = 0; x < PreviewWidth; x++)
                {
                    float u = ((x + 0.5f) / PreviewWidth * 2f - 1f) * 1.15f * aspect;
                    float3 origin = new(u, -v, 3.4f);
                    float3 direction = new(0f, 0f, -1f);

                    float depth = 0f;
                    bool hit = false;
                    float distance = 0f;
                    for (int step = 0; step < 56; step++)
                    {
                        float3 point = new(
                            origin.x + direction.x * depth,
                            origin.y + direction.y * depth,
                            origin.z + direction.z * depth);
                        distance = shape.Evaluate(point);
                        if (distance < 0.0015f)
                        {
                            hit = true;
                            break;
                        }
                        depth += MathF.Max(distance, 0.002f);
                        if (depth > 7f) break;
                    }

                    float4 color;
                    if (hit)
                    {
                        float3 point = new(
                            origin.x + direction.x * depth,
                            origin.y + direction.y * depth,
                            origin.z + direction.z * depth);
                        float3 normal = shape.Normal(point);
                        float diffuse = MathF.Max(0f, normal.x * sun.x + normal.y * sun.y + normal.z * sun.z);
                        float rim = MathF.Pow(1f - MathF.Max(0f, -direction.z), 2.0f);
                        float ambient = 0.18f + 0.12f * MathF.Max(0f, normal.y);
                        float light = ambient + diffuse * 0.85f;
                        color = new float4(
                            baseColor.x * light + rim * 0.12f,
                            baseColor.y * light + rim * 0.14f,
                            baseColor.z * light + rim * 0.18f,
                            1f);
                    }
                    else
                    {
                        float gradient = 1f - (y / (float)PreviewHeight);
                        color = new float4(
                            0.075f + gradient * 0.055f,
                            0.085f + gradient * 0.06f,
                            0.10f + gradient * 0.075f,
                            1f);
                    }

                    int index = y * stride + x * 4;
                    pixels[index + 0] = ToByte(color.z);
                    pixels[index + 1] = ToByte(color.y);
                    pixels[index + 2] = ToByte(color.x);
                    pixels[index + 3] = 255;
                }
            }
        }

        private static byte ToByte(float value)
            => (byte)Math.Clamp((int)(value * 255f), 0, 255);

        private void DisposePreview()
        {
            preview?.Dispose();
            preview = null;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            Editor.NodeValueChanged -= OnNodeValueChanged;
            Editor.GraphStructureChanged -= OnGraphStructureChanged;
            DisposePreview();
            base.Dispose();
        }
    }
}
