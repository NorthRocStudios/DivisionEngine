//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.Components;
using DivisionEngine.Components.SDFs.Effects;
using DivisionEngine.MathLib;
using DivisionEngine.Rendering.Denoising;
using DivisionEngine.Rendering.VoxelSDF;
using DivisionEngine.Settings;
using DivisionEngine.Systems;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Diagnostics;
using Vortice.D3DCompiler;
using System.Runtime.InteropServices;
using Window = Silk.NET.Windowing.Window;

namespace DivisionEngine.Rendering
{

    /// <summary>
    /// SDF Render pipeline for Division Engine.
    /// </summary>
    public class RenderPipeline
    {
        /// <summary>
        /// Represents the debug mode of the render pipeline, if any.
        /// </summary>
        public enum DebugMode
        {
            None = 0, Depth = 1, WorldNormals = 2, ObjectID = 3, RaySteps = 4,
            Shadows = 5, BRDF = 6, Specular = 7, Diffuse = 8, MipLevels = 9,
        }

        // Special variables

        /// <summary>
        /// The current render pipeline instance running.
        /// </summary>
        public static RenderPipeline? Instance { get; private set; }

        /// <summary>
        /// Synchronization lock for thread safety
        /// </summary>
        public readonly Lock SyncLock = new Lock();
        private bool closeWindowWithCloseEvent = true;

        // OpenGL variables
        private GL? gl;
        private uint glTexture, glShaderProgram;
        private static bool deviceLost;
        private bool presentLogged;
        private bool announcedShaderCompile;

        // Diagnostic for "black/white window" reports: set DIVISION_LOG_PRESENT=1 to log one
        // read-back of the presented frame (what GL actually holds vs. the CPU colour buffer).
        private static readonly bool presentLogEnabled =
            System.Environment.GetEnvironmentVariable("DIVISION_LOG_PRESENT") is "1" or "true";
        private float currentDpiScale = 1f;

        // Rendering variables

        /// <summary>
        /// Reference to the bound world that the renderer is using.
        /// </summary>
        public World? boundWorld;

        /// <summary>
        /// Reference to the renderer window handle.
        /// </summary>
        public IWindow? RendererWindow { get; private set; }

        /// <summary>
        /// Reference to the renderer's DivisionCompute device and context. Every
        /// compute pass, copy and readback in the frame goes through it.
        /// </summary>
        public DivisionComputeContext? Context { get; private set; }

        /// <summary>
        /// Whether the renderer is ready to process input or not.
        /// </summary>
        public bool InputReady { get; private set; } = false;

        /// <summary>
        /// Event to handle window close actions.
        /// </summary>
        public event Action? Close;

        /// <summary>
        /// Event called when input context is created, for handler setup on other threads, ex. Avalonia UI Thread.
        /// </summary>
        public event Action<IInputContext>? InputContextCreated;

        /// <summary>
        /// The current debug mode of the render pipeline.
        /// </summary>
        public DebugMode CurrentDebugMode { get; set; } = DebugMode.None;

        /// <summary>
        /// Buffer of all of the object IDs per pixel on screen.
        /// </summary>
        public uint2[]? ObjectIDs { get; private set; }

        /// <summary>
        /// Buffer of all the editor handle IDs overlayed per pixel on screen.
        /// </summary>
        public uint[]? HandleIds { get; private set; }

        /// <summary>
        /// Buffer of all the icon IDs overlayed per pixel on screen.
        /// </summary>
        public uint[]? IconIds { get; private set; }

        /// <summary>
        /// Buffer of all the custom shape IDs overlayed per pixel on screen.
        /// </summary>
        public uint[]? CustomShapeIds { get; private set; }

        public enum RunMode { Windowed, Embedded }
        public RunMode Mode { get; private set; } = RunMode.Windowed;

        // Embedded-mode state
        private byte[]? embeddedBgraBuffer;
        private readonly Lock embeddedBufferLock = new();
        public int EmbeddedWidth { get; private set; } = 1;
        public int EmbeddedHeight { get; private set; } = 1;
        public event Action? FrameAvailable;

        private Thread? embeddedThread;
        private volatile bool embeddedRunning;
        private int embeddedBufferWidth = 0;
        private int embeddedBufferHeight = 0;

        // Rendering storage
        private RWTexture2D<float>? lowResDepthTex;
        private RWTexture2D<float4>? lowResRenderTex; // Optional, for debugging
        private RWTexture2D<float4>? renderTex;
        private RWTexture2D<float4>? depthNormalsTex;
        private RWBuffer<uint2>? objectIdBuffer;
        private RWBuffer<uint>? handleIdBuffer;

        // Buffer storage
        private StructuredBuffer<SDFObjectDTO>? sdfObjBuffer;
        private StructuredBuffer<SDFLightDTO>? lightsBuffer;
        private Buffer<uint>? textureBuffer;
        private StructuredBuffer<TextureMetadata>? textureMetaBuffer;

        // Terrain storage
        private Buffer<float>? terrainHeightBuffer;
        private StructuredBuffer<TerrainDTO>? terrainMetadataBuffer;
        private bool rebuildTerrainBuffer = true;

        // Voxelized SDF path
        private VoxelClipmapRenderer? voxelClipmap;

        // Post-processing storage
        private RWTexture2D<float4>? fxaaInputTex;

        // Compiled kernels, cached per shader type: compilation is expensive and the
        // shader objects are stateless between dispatches.
        private readonly Dictionary<Type, DivisionShader> shaders = [];

        /// <summary>
        /// Rendered pixels buffer.
        /// </summary>
        public float4[]? Pixels { get; private set; }

        /// <summary>
        /// Depth and world normals (r - depth, gba - normalized world normal vector).
        /// </summary>
        public float4[]? DepthNormalPixels { get; private set; }

        /// <summary>
        /// When set, renders are reproducible: the checkerboard (temporal) path is
        /// disabled and the caller drives a fixed number of frames at a fixed time
        /// step. Set by <c>--cli shot --fixed-frame</c> before rendering.
        /// </summary>
        public static bool DeterministicCapture { get; set; }

        /// <summary>
        /// Is checkerboard rendering enabled (renders half the pixels each frame).
        /// </summary>
        public static bool CheckerboardRenderingEnabled => !DeterministicCapture && (EngineSettings.Instance?.CheckerboardRendering ?? false);

        /// <summary>
        /// Optional runtime override for the voxel rendering toggle. CLI test hooks use
        /// this so a headless run never rewrites the user's persisted settings.
        /// </summary>
        public static bool? VoxelModeOverride { get; set; }

        /// <summary>Optional runtime override for the voxel grid resolution.</summary>
        public static int? VoxelResolutionOverride { get; set; }

        /// <summary>True when the voxelized SDF path should render this frame.</summary>
        public static bool VoxelRenderingEnabled =>
            VoxelModeOverride ?? (EngineSettings.Instance?.VoxelSdfRendering ?? false);

        /// <summary>Grid points along each axis of a voxel clipmap level.</summary>
        public static int VoxelGridResolution =>
            VoxelResolutionOverride ?? (EngineSettings.Instance?.VoxelGridResolution ?? 96);

        /// <summary>Maximum newly baked cells processed per frame while streaming.</summary>
        public static int VoxelUpdateCellBudget =>
            EngineSettings.Instance?.VoxelUpdateCellBudget ?? VoxelClipmapRenderer.DefaultCellBudget;

        /// <summary>The voxel clipmap renderer owned by this pipeline, if it has run.</summary>
        public VoxelClipmapRenderer? VoxelClipmap => voxelClipmap;

        /// <summary>
        /// Number of frames that finished rendering. Used by the editor to defer
        /// background shader preloading until the renderer has compiled what it needs
        /// for the first frame, so the two never duplicate expensive compiles.
        /// </summary>
        public static long FramesRendered { get; private set; }

        // Time measurement

        /// <summary>
        /// Time the renderer has elapsed.
        /// </summary>
        public static double Time { get; private set; }

        /// <summary>
        /// Time between frames.
        /// </summary>
        public static double DeltaTime { get; private set; }

        // Handles
        private float3? editorHandlePosition = null;
        private float editorHandleScale = 1.0f;
        private uint currentHoveredHandle = 0;

        // Icons
        private readonly Dictionary<uint, (float3 position, IconType iconType, float3 direction)> iconsToRender = [];
        private RWBuffer<uint>? iconIdBuffer;

        // Custom Shapes
        private readonly Dictionary<uint, HandleShape> customShapes = [];
        private RWBuffer<uint>? customShapeIdBuffer;
        // One-element structured buffer used to hand a single HandleShape to the
        // shape shader (struct uniforms are not supported by the packed cbuffer).
        private StructuredBuffer<HandleShape>? customShapeBuffer;

        // Denoising
        private Buffer<float>? kernelBuffer; // Reconstruction kernel
        private RWTexture2D<float4>? postProcessTex;
        // Second A-Trous target: the ping/pong pair must never alias, otherwise the
        // 3x3 wavelet reads texels its own dispatch is still writing (a UAV race that
        // made renders differ run to run).
        private RWTexture2D<float4>? aTrousPongTex;

        // DivisionCompute post-processing. It shares the pipeline device, so the
        // effect chain runs GPU-to-GPU with no upload or readback.
        private DivisionPostProcess? divisionPostProcess;
        private bool divisionPostUnavailable;

        // Input context
        private IInputContext? inputContext;

        // Textures
        private bool rebuildTextureBuffer = true;

        /// <summary>
        /// Create a new render pipeline.
        /// </summary>
        public RenderPipeline()
        {
            Instance = this;
            TextureSystem.UpdatedTextureData += () => rebuildTextureBuffer = true;
            TerrainSystem.UpdatedTerrainData += () => rebuildTerrainBuffer = true;
        }

        /// <summary>
        /// Binds the WorldManager.CurrentWorld to this render pipeline.
        /// </summary>
        /// <returns>If the world was successfully bound</returns>
        public bool BindCurrentWorld()
        {
            if (WorldManager.CurrentWorld != null)
            {
                boundWorld = WorldManager.CurrentWorld;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears the bound world reference. Called during project teardown so the
        /// render thread doesn't keep operating on a world whose systems have been
        /// unloaded. Safe to call from any thread.
        /// </summary>
        public void UnbindWorld()
        {
            lock (SyncLock)
            {
                boundWorld = null;
            }
        }

        /// <summary>
        /// Updates the DPI scaling factor for input coordinate conversion.
        /// </summary>
        public void UpdateDpiScale(float scale) => currentDpiScale = scale;

        #region editorDrawing

        /// <summary>
        /// Sets the position where editor handles should be rendered.
        /// </summary>
        public void ShowHandles(float3 position, float scale = 1.0f)
        {
            editorHandlePosition = position;
            editorHandleScale = scale;
        }

        /// <summary>
        /// Hides the editor handles.
        /// </summary>
        public void HideHandles() => editorHandlePosition = null;

        /// <summary>
        /// Gets a handle index at a screen coordinate.
        /// </summary>
        /// <param name="screenX">X position on the render target</param>
        /// <param name="screenY">Y position on the render target</param>
        /// <returns>Handle uint index</returns>
        public uint GetHandleAtPosition(int screenX, int screenY)
        {
            // Check if we're in embedded mode
            int flippedY, index;
            if (Mode == RunMode.Embedded)
            {
                if (HandleIds == null || screenX < 0 || screenY < 0 ||
                    screenX >= EmbeddedWidth || screenY >= EmbeddedHeight) return 0;

                flippedY = EmbeddedHeight - 1 - screenY;
                index = screenX + flippedY * EmbeddedWidth;

                // Validate index bounds
                if (index < 0 || index >= HandleIds.Length) return 0;
                return HandleIds[index];
            }

            // Original windowed mode
            if (RendererWindow == null || HandleIds == null || screenX < 0 || screenY < 0 ||
                screenX >= RendererWindow?.Size.X || screenY >= RendererWindow?.Size.Y) return 0;

            int width = RendererWindow!.Size.X;
            flippedY = RendererWindow.Size.Y - 1 - screenY;
            index = screenX + flippedY * width;

            if (index < 0 || index >= HandleIds.Length) return 0;
            return HandleIds[index];
        }

        /// <summary>
        /// Returns an icon index if there is one at a specified screen point.
        /// </summary>
        /// <param name="screenX">Rendered pixel X coord</param>
        /// <param name="screenY">Rendered pixel Y coord</param>
        /// <returns>Icon index</returns>
        public uint GetIconAtPosition(int screenX, int screenY)
        {
            int flippedY, index;
            if (Mode == RunMode.Embedded)
            {
                if (iconIdBuffer == null || IconIds == null || screenX < 0 || screenY < 0 ||
                    screenX >= EmbeddedWidth || screenY >= EmbeddedHeight) return 0;

                flippedY = EmbeddedHeight - 1 - screenY;
                index = screenX + flippedY * EmbeddedWidth;

                if (index < 0 || index >= IconIds.Length) return 0;
                return IconIds[index];
            }

            if (RendererWindow == null || iconIdBuffer == null || IconIds == null || screenX < 0 || screenY < 0 ||
                screenX >= RendererWindow.Size.X || screenY >= RendererWindow.Size.Y) return 0;

            int width = RendererWindow.Size.X;
            int height = RendererWindow.Size.Y;
            flippedY = height - 1 - screenY;
            index = screenX + flippedY * width;

            if (index < 0 || index >= IconIds.Length) return 0;
            return IconIds[index];
        }

        /// <summary>
        /// Returns a custom shape index if there is one at a specified screen point.
        /// </summary>
        /// <param name="screenX">Rendered pixel X coord</param>
        /// <param name="screenY">Rendered pixel Y coord</param>
        /// <returns>Custom shape index</returns>
        public uint GetCustomShapeAtPosition(int screenX, int screenY)
        {
            int flippedY, index;
            if (Mode == RunMode.Embedded)
            {
                if (customShapeIdBuffer == null || CustomShapeIds == null || screenX < 0 || screenY < 0 ||
                    screenX >= EmbeddedWidth || screenY >= EmbeddedHeight) return 0;

                flippedY = EmbeddedHeight - 1 - screenY;
                index = screenX + flippedY * EmbeddedWidth;

                if (index < 0 || index >= CustomShapeIds.Length) return 0;
                return CustomShapeIds[index];
            }

            if (RendererWindow == null || customShapeIdBuffer == null || CustomShapeIds == null || screenX < 0 || screenY < 0 ||
                screenX >= RendererWindow.Size.X || screenY >= RendererWindow.Size.Y) return 0;

            int width = RendererWindow.Size.X;
            int height = RendererWindow.Size.Y;
            flippedY = height - 1 - screenY;
            index = screenX + flippedY * width;

            if (index < 0 || index >= CustomShapeIds.Length) return 0;
            return CustomShapeIds[index];
        }

        /// <summary>
        /// Notifies the render pipeline of the current mouse position to update focused handles.
        /// </summary>
        /// <param name="mouseX">Mouse X position</param>
        /// <param name="mouseY">Mouse Y position</param>
        public void UpdateHoveredHandle(int mouseX, int mouseY)
        {
            lock (SyncLock)
            {
                if (HandleIds == null) return;

                int width, height;
                if (Mode == RunMode.Embedded)
                {
                    width = EmbeddedWidth;
                    height = EmbeddedHeight;
                }
                else if (RendererWindow != null)
                {
                    width = RendererWindow.Size.X;
                    height = RendererWindow.Size.Y;
                }
                else return;

                // Validate coordinates first
                if (mouseX < 0 || mouseX >= width || mouseY < 0 || mouseY >= height)
                {
                    currentHoveredHandle = 0;
                    return;
                }

                int flippedY = height - 1 - mouseY;
                int index = mouseX + flippedY * width;

                // Validate the index is within the array bounds
                if (index < 0 || index >= HandleIds.Length)
                {
                    currentHoveredHandle = 0;
                    return;
                }
                currentHoveredHandle = HandleIds[index];
            }
        }

        /// <summary>
        /// Shows an icon at a position in the world.
        /// </summary>
        /// <remarks>(editor only)</remarks>
        /// <param name="position">3D icon world position</param>
        /// <param name="icon">Icon type to display</param>
        /// <param name="direction">Diretion the icon points in</param>
        /// <param name="entityId">Entity ID icon is attached to</param>
        public void ShowIcon(float3 position, IconType icon, float3 direction, uint entityId)
        {
            lock (SyncLock)
            {
                iconsToRender[entityId] = (position, icon, direction);
            }
        }

        /// <summary>
        /// Hides an icon from view in the editor.
        /// </summary>
        /// <param name="entityId">Entity ID to hide icon for</param>
        public void HideIcon(uint entityId)
        {
            lock (SyncLock)
            {
                iconsToRender.Remove(entityId);
            }
        }

        /// <summary>
        /// Clears all rendered icons in the editor.
        /// </summary>
        public void ClearIcons()
        {
            lock (SyncLock)
            {
                iconsToRender.Clear();
            }
        }

        #endregion editorDrawing
        #region embeddedRendering

        /// <summary>
        /// Starts the render pipeline in embedded mode.
        /// </summary>
        /// <param name="targetFps">Target frames per second to run at</param>
        public void RunEmbedded(double targetFps)
        {
            Mode = RunMode.Embedded;
            deviceLost = false;
            WarnIfShaderCacheCold();
            Context = EnsureContext();
            Debug.Info("Renderer: Using DivisionCompute D3D11 device");
            Context.DeviceLost += Device_DeviceLost;

            embeddedRunning = true;
            embeddedThread = new Thread(() => EmbeddedLoop(targetFps))
            {
                IsBackground = true,
                Name = "DivisionEmbeddedRenderer"
            };
            embeddedThread.Start();
        }

        /// <summary>
        /// Stops the embedded renderer.
        /// </summary>
        public void StopEmbedded()
        {
            embeddedRunning = false;
            embeddedThread?.Join(500);
        }

        /// <summary>
        /// Called by the hosting control whenever its size changes.
        /// </summary>
        public void SetEmbeddedViewportSize(int width, int height)
        {
            lock (SyncLock)
            {
                EmbeddedWidth = math.max(1, width);
                EmbeddedHeight = math.max(1, height);
            }
        }

        private void EmbeddedLoop(double targetFps)
        {
            Stopwatch sw = Stopwatch.StartNew();
            double last = sw.Elapsed.TotalSeconds;
            int frameCount = 0;
            double fpsUpdateTimer = 0;

            while (embeddedRunning)
            {
                double fps = EngineSettings.Instance?.MaxFPS > 0 ? EngineSettings.Instance.MaxFPS : targetFps;
                double frameBudget = 1.0 / fps;
                double now = sw.Elapsed.TotalSeconds;
                double delta = now - last;

                if (delta < frameBudget)
                {
                    Thread.Sleep(1);
                    continue;
                }

                last = now;
                frameCount++;
                fpsUpdateTimer += delta;

                // Update FPS counter every second
                if (fpsUpdateTimer >= 1.0)
                {
                    // This will be picked up by TimeSystem's Render() method
                    frameCount = 0;
                    fpsUpdateTimer = 0;
                }

                // Snapshot ONCE per frame
                int frameWidth, frameHeight;
                lock (SyncLock)
                {
                    frameWidth = EmbeddedWidth;
                    frameHeight = EmbeddedHeight;
                }

                // IMPORTANT: This will set DeltaTime and Time for the TimeSystem
                RenderFrame(delta, frameWidth, frameHeight, presentToGL: false);
                ConvertPixelsToBgra(frameWidth, frameHeight);
                FrameAvailable?.Invoke();
            }
        }

        #endregion embeddedRendering

        /// <summary>
        /// Creates the DivisionCompute device on first use.
        /// </summary>
        private DivisionComputeContext EnsureContext() => Context ??= new DivisionComputeContext();

        /// <summary>
        /// Warns once at startup when the on-disk shader cache is empty: the first frame then
        /// has to compile the raymarch shader, which takes minutes and looks like a hang.
        /// </summary>
        private static void WarnIfShaderCacheCold()
        {
            try
            {
                if (System.Environment.GetEnvironmentVariable("DIVISION_DISABLE_SHADER_CACHE") is "1" or "true")
                {
                    Debug.Warning("Renderer: shader cache disabled - the first frame compiles every shader " +
                                  "and can take several minutes");
                    return;
                }

                string cacheDirectory = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "DivisionEngine", "ShaderCache");

                if (!System.IO.Directory.Exists(cacheDirectory) ||
                    System.IO.Directory.GetFiles(cacheDirectory).Length == 0)
                {
                    Debug.Warning("Renderer: shader cache is empty - the first frame compiles the raymarch " +
                                  "shader and can take several minutes. Later runs will start quickly.");
                }
            }
            catch
            {
                // Diagnostics must never prevent startup.
            }
        }

        /// <summary>
        /// Returns the compiled kernel for a shader type, compiling it on first use.
        /// </summary>
        private DivisionShader GetShader(Type shaderType)
        {
            if (shaders.TryGetValue(shaderType, out DivisionShader? shader))
                return shader;

            // Compilation happens inside the first frame, so show something in the window while
            // the (potentially multi-minute) cold-cache compile runs, and log how long it took.
            bool announce = !announcedShaderCompile;
            string previousTitle = string.Empty;
            if (announce)
            {
                announcedShaderCompile = true;
                previousTitle = RendererWindow?.Title ?? string.Empty;
                if (RendererWindow != null)
                    RendererWindow.Title = "Division Engine - compiling shaders";
            }

            Debug.Info($"Renderer: compiling '{shaderType.Name}'");
            System.Diagnostics.Stopwatch compileTimer = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                shader = CompileShaderWithFlags(shaderType);
            }
            finally
            {
                compileTimer.Stop();
                if (announce && RendererWindow != null)
                    RendererWindow.Title = previousTitle;
            }

            Debug.Info($"Renderer: '{shaderType.Name}' ready in {compileTimer.ElapsedMilliseconds} ms" +
                       (compileTimer.ElapsedMilliseconds > 5000 ? " (compiled, not a cache hit)" : string.Empty));
            shaders[shaderType] = shader;
            return shader;
        }

        /// <summary>
        /// Compiles a shader through the explicit translation/compile path. The voxel
        /// raymarch shader explicitly requests O3 because D3DCompiler's O1 default for
        /// this shader produced a much slower runtime. The O3 bytecode is cached, so
        /// the long first compile is paid only once.
        /// </summary>
        private DivisionShader CompileShaderWithFlags(Type shaderType)
        {
            if (shaderType != typeof(VoxelSDFShader3D))
                return Context!.CompileShader(shaderType);

            HLSLTranslationResult translation = Context!.Compiler.TranslateShader(shaderType);
            if (!translation.IsSuccess)
                throw new InvalidOperationException(translation.ErrorMessage);

            HLSLCompilationResult compilation = DivisionShaderCompiler.CompileHLSL(
                translation, ShaderFlags.OptimizationLevel3);
            if (!compilation.IsSuccess)
                throw new InvalidOperationException(compilation.DebugMessage);

            return Context.CreateShader(compilation, shaderType);
        }

        /// <summary>
        /// Drops every compiled shader so the next frame picks up freshly compiled
        /// bytecode. Call after recompiling shaders (the bytecode cache already holds
        /// the new entries).
        /// </summary>
        public void ReloadShaders()
        {
            int dropped;
            lock (SyncLock)
            {
                dropped = shaders.Count;
                foreach (DivisionShader shader in shaders.Values)
                    shader.Dispose();
                shaders.Clear();

                // The clipmap owns its own bake shader and grid textures; drop them
                // so the next frame rebuilds both against fresh code.
                voxelClipmap?.Dispose();
                voxelClipmap = null;

                // The post-processing shader may have been the one that failed before.
                divisionPostUnavailable = false;
            }

            Debug.Info($"Render pipeline: dropped {dropped} compiled shader(s) for reload");
        }

        /// <summary>
        /// Renders an exact number of frames at a fixed time step and leaves the
        /// colour buffer in <see cref="Pixels"/>.
        /// </summary>
        /// <remarks>
        /// Combined with <see cref="DeterministicCapture"/> and a seeded RNG this
        /// produces identical output on every run, which is what the golden-image
        /// gate compares against. Unlike <see cref="RenderSingleFrame"/> the frame
        /// count is not a warm-up heuristic: frame <c>i</c> always sees the same
        /// accumulated time, so the render is pinned to a known point in the
        /// animation.
        /// </remarks>
        /// <param name="width">Render width in pixels</param>
        /// <param name="height">Render height in pixels</param>
        /// <param name="frameCount">Number of frames to advance the pipeline by</param>
        /// <param name="fixedDelta">Fixed time step, in seconds, used for every frame</param>
        public float4[]? RenderFixedFrames(int width, int height, int frameCount, double fixedDelta)
        {
            EnsureContext();
            if (boundWorld == null) BindCurrentWorld();
            if (boundWorld == null) throw new InvalidOperationException("No world is bound.");
            if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount), "At least one frame must be rendered.");
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "The render resolution must be positive.");

            for (int i = 0; i < frameCount; i++)
                RenderFrame(fixedDelta, width, height, presentToGL: false);
            return Pixels;
        }

        /// <summary>
        /// Renders one frame offscreen and leaves the colour buffer in <see cref="Pixels"/>.
        /// Used by the Division CLI screenshot command.
        /// </summary>
        public float4[]? RenderSingleFrame(int width, int height)
        {
            EnsureContext();
            if (boundWorld == null) BindCurrentWorld();
            if (boundWorld == null) throw new InvalidOperationException("No world is bound.");

            // Render a couple of frames so lazily created buffers are warm.
            RenderFrame(1.0 / 60.0, width, height, presentToGL: false);
            RenderFrame(1.0 / 60.0, width, height, presentToGL: false);
            return Pixels;
        }

        #region benchmark

        /// <summary>
        /// Result of an offscreen render benchmark.
        /// </summary>
        public sealed class BenchmarkResult(int width, int height, int frames, double averageMs, double medianMs, double p95Ms)
        {
            /// <summary>Render width in pixels.</summary>
            public int Width { get; } = width;
            /// <summary>Render height in pixels.</summary>
            public int Height { get; } = height;
            /// <summary>Number of measured frames.</summary>
            public int Frames { get; } = frames;
            /// <summary>Mean frame time in milliseconds.</summary>
            public double AverageMs { get; } = averageMs;
            /// <summary>Median frame time in milliseconds.</summary>
            public double MedianMs { get; } = medianMs;
            /// <summary>95th percentile frame time in milliseconds.</summary>
            public double P95Ms { get; } = p95Ms;
            /// <summary>Frames per second derived from the mean frame time.</summary>
            public double Fps => AverageMs > 0 ? 1000.0 / AverageMs : 0;
        }

        /// <summary>
        /// Renders frames offscreen at a fixed resolution and measures frame times.
        /// Used by the Division CLI benchmark command so performance can be measured
        /// without opening the editor or depending on its window layout.
        /// </summary>
        /// <param name="width">Render width in pixels</param>
        /// <param name="height">Render height in pixels</param>
        /// <param name="frames">Number of frames to measure</param>
        /// <param name="warmupFrames">Frames rendered before measuring (shader/JIT warmup)</param>
        /// <param name="cameraMovePerFrame">Optional world-space X movement applied before each frame.</param>
        public BenchmarkResult Benchmark(int width, int height, int frames, int warmupFrames = 10,
            float cameraMovePerFrame = 0f)
        {
            EnsureContext();
            if (boundWorld == null) BindCurrentWorld();
            if (boundWorld == null) throw new InvalidOperationException("No world is loaded to benchmark.");

            for (int i = 0; i < warmupFrames; i++)
            {
                if (cameraMovePerFrame != 0f) MoveActiveCamera(cameraMovePerFrame);
                RenderFrame(1.0 / 60.0, width, height, presentToGL: false);
            }

            double[] times = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                if (cameraMovePerFrame != 0f) MoveActiveCamera(cameraMovePerFrame);
                Stopwatch sw = Stopwatch.StartNew();
                RenderFrame(1.0 / 60.0, width, height, presentToGL: false);
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(times);
            double average = 0;
            foreach (double t in times) average += t;
            average /= frames;

            double median = times[frames / 2];
            double p95 = times[Math.Min(frames - 1, (int)(frames * 0.95))];
            return new BenchmarkResult(width, height, frames, average, median, p95);
        }

        private void MoveActiveCamera(float deltaX)
        {
            if (boundWorld == null) return;
            foreach ((uint id, Transform transform) in boundWorld.QueryData<Transform>())
            {
                if (!boundWorld.HasComponent<Camera>(id)) continue;
                Camera? camera = boundWorld.GetComponent<Camera>(id);
                if (camera is not { isActive: true }) continue;
                transform.position += new float3(deltaX, 0f, 0f);
                return;
            }
        }

        #endregion benchmark

        /// <summary>
        /// Initializes and runs the render window.
        /// </summary>
        /// <param name="defaultFPS">The default fallback FPS if engine settings cannot be located</param>
        /// <param name="editorMode">If the render pipeline is running in editor mode</param>
        /// <remarks>This method creates a render window with default options, sets up event handlers for
        /// loading and rendering.</remarks>
        public void Run(double defaultFPS, bool editorMode)
        {
            deviceLost = false;
            WarnIfShaderCacheCold();
            try
            {
                WindowOptions options = WindowOptions.Default;
                if (editorMode)
                {
                    options.TopMost = true;
                    options.WindowBorder = WindowBorder.Hidden;
                }
                options.Title = "SDF Scene";
                options.IsVisible = true;
                options.VSync = true;
                options.ShouldSwapAutomatically = true;

                if (EngineSettings.Instance.MaxFPS > 0)
                    options.UpdatesPerSecond = EngineSettings.Instance.MaxFPS;
                else options.UpdatesPerSecond = defaultFPS;

                // Load render window settings
                options.Size = new Silk.NET.Maths.Vector2D<int>(EngineSettings.Instance.ResolutionWidth, EngineSettings.Instance.ResolutionHeight);

                RendererWindow = Window.Create(options);
                Debug.Info($"Renderer: Created Render Window on thread {System.Environment.CurrentManagedThreadId}");

                closeWindowWithCloseEvent = true;
                RendererWindow.Load += OnLoad;
                RendererWindow.Render += OnRender;
                RendererWindow.Closing += OnClosing;

                // Ensure texture buffer rebuild
                rebuildTextureBuffer = true;

                Debug.Info("Renderer: Starting window run loop");
                RendererWindow.Run();
            }
            catch (Exception ex)
            {
                Debug.Error($"Renderer: Failed to run window", ex);
                throw;
            }
        }

        /// <summary>
        /// Called when the renderer window is closing.
        /// </summary>
        private void OnClosing()
        {
            lock (SyncLock)
            {
                CleanupResources();
                if (closeWindowWithCloseEvent) Close?.Invoke(); // Invoke the close event if there are any subscribers
            }
        }

        /// <summary>
        /// Stops the renderer window from running.
        /// </summary>
        /// <returns>If the window was successfully closed</returns>
        public bool Stop()
        {
            lock (SyncLock)
            {
                if (RendererWindow != null)
                {
                    closeWindowWithCloseEvent = false;
                    RendererWindow!.Close();
                    RendererWindow = null;
                    closeWindowWithCloseEvent = true;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Called on render window load.
        /// </summary>
        private void OnLoad()
        {
            lock (SyncLock)
            {
                if (RendererWindow == null) return;
                gl = GL.GetApi(RendererWindow);

                // Initialize OpenGL context
                Debug.Info("Renderer: Initialize OpenGL Context");
                gl.GenTextures(1, out glTexture);
                gl.BindTexture(TextureTarget.Texture2D, glTexture);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

                // Load DivisionCompute device
                Context = EnsureContext();
                Debug.Info("Renderer: Using DivisionCompute D3D11 device");
                Context.DeviceLost += Device_DeviceLost;

                Debug.Info("Renderer: Compiling OpenGL Shader Program");
                glShaderProgram = CompileShaders();
                gl!.GenVertexArrays(1, out uint vao);
                gl.BindVertexArray(vao);
                Debug.Info("Renderer: VAO Bound");

                try
                {
                    Debug.Info("Renderer: Creating input context...");
                    inputContext ??= RendererWindow.CreateInput(); // Must create input context on same thread as render window!
                    if (inputContext != null)
                    {
                        Debug.Info("Renderer: Input context created successfully");
                        InputContextCreated?.Invoke(inputContext);
                    }
                    else Debug.Error("Renderer: Failed to create input context");
                }
                catch (Exception ex)
                {
                    Debug.Error($"Renderer: Exception creating input context", ex);
                }
                InputReady = true;
            }
        }

        /// <summary>
        /// Debug and attempt to rebuild on device lost.
        /// </summary>
        private void Device_DeviceLost(string reason)
        {
            deviceLost = true;
            Debug.Error($"Renderer: Graphics Device Lost!\nCause: {reason}");
            lock (SyncLock)
            {
                try { Context?.Dispose(); }
                catch (Exception ex) { Debug.Warning("Renderer: failed to dispose the lost context", ex); }
                Context = null;

                // The input context belongs to the window, not the device, so keep it alive.
                CleanupResources(disposeInputContext: false);
            }
        }

        /// <summary>
        /// Rebuilds the compute device and every resource that belonged to a lost one.
        /// </summary>
        private void RecoverDevice()
        {
            lock (SyncLock)
            {
                try
                {
                    Context?.Dispose();
                    Context = null;
                    CleanupResources(disposeInputContext: false);
                    Context = EnsureContext();
                    deviceLost = false;
                    Debug.Info("Renderer: recovered from device loss - device and resources recreated");
                }
                catch (Exception ex)
                {
                    Debug.Error("Renderer: device recovery failed, will retry on the next frame", ex);
                    deviceLost = true;
                }
            }
        }

        #region rendering

        private void OnRender(double delta)
        {
            if (RendererWindow == null || RendererWindow.IsClosing) return;
            lock (SyncLock) // Apply render window settings
            {
                RendererWindow!.VSync = EngineSettings.Instance.VSync;
                RendererWindow.FramesPerSecond = EngineSettings.Instance.MaxFPS;
            }

            int texWidth = RendererWindow.Size.X;
            int texHeight = RendererWindow.Size.Y;
            if (texWidth < 1 || texHeight < 1) return;
            RenderFrame(delta, texWidth, texHeight, presentToGL: true);
        }


        /// <summary>
        /// Executes the render pipeline frame.
        /// </summary>
        /// <param name="delta">Travel delta between frames in seconds</param>
        /// <param name="texWidth">Rendered frame width</param>
        /// <param name="texWidth">Rendered frame height</param>
        /// <param name="presentToGL">Whether or not to render to a Silk.NET OpenGL target</param>
        private void RenderFrame(double delta, int texWidth, int texHeight, bool presentToGL)
        {
            DeltaTime = delta; // Track frame time
            Time += delta;

            if (deviceLost) RecoverDevice();

            if (Context == null)
            {
                Debug.Warning("Renderer: No graphics device available, skipping rendering");
                return;
            }

            // DivisionCompute exposes device removal as a poll instead of an event.
            Context.CheckDeviceLost();

            if (boundWorld == null) return;

            try
            {
                boundWorld.CallRender();
            }
            catch (Exception ex)
            {
                // A system's Render() can race the editor thread mutating the world; an
                // unhandled exception here would kill the process. Skip this frame instead.
                Debug.Error("Renderer: a system threw during Render() - skipping this frame", ex);
                return;
            }
            int lowResWidth = math.max(1, texWidth / 4),
                lowResHeight = math.max(1, texHeight / 4);

            SDFWorldDTO worldDTO;
            SDFObjectDTO[] sdfObjDTO;
            SDFLightDTO[] sdfLightsDTO;
            lock (SyncLock)
            {
                worldDTO = SDFRenderSystem.PreparedWorldDTO;
                sdfObjDTO = SDFRenderSystem.PreparedSDFObjectsDTO;
                sdfLightsDTO = SDFRenderSystem.PreparedLightsDTO;
            }
            if (sdfObjDTO.Length < 1) return;

            try
            {
                lock (SyncLock)
                {
                    // Build render texture
                    if (renderTex == null || renderTex.Width != texWidth || renderTex.Height != texHeight || Pixels == null)
                    {
                        renderTex?.Dispose();
                        renderTex = Context!.CreateRWTexture2D<float4>(texWidth, texHeight);
                        Pixels = new float4[texWidth * texHeight];
                    }

                    // Build low res depth prepass buffer
                    if (lowResDepthTex == null || lowResDepthTex.Width != lowResWidth || lowResDepthTex.Height != lowResHeight)
                    {
                        lowResDepthTex?.Dispose();
                        lowResDepthTex = Context!.CreateRWTexture2D<float>(lowResWidth, lowResHeight);
                    }

                    // Optional: For debugging the low-res pass
                    if (lowResRenderTex == null || lowResRenderTex.Width != lowResWidth || lowResRenderTex.Height != lowResHeight)
                    {
                        lowResRenderTex?.Dispose();
                        lowResRenderTex = Context!.CreateRWTexture2D<float4>(lowResWidth, lowResHeight);
                    }

                    // Build denoised texture (for post-process)
                    if (postProcessTex == null || postProcessTex.Width != texWidth || postProcessTex.Height != texHeight)
                    {
                        postProcessTex?.Dispose();
                        postProcessTex = Context!.CreateRWTexture2D<float4>(texWidth, texHeight);
                    }

                    // Second A-Trous target (kept distinct from postProcessTex)
                    if (aTrousPongTex == null || aTrousPongTex.Width != texWidth || aTrousPongTex.Height != texHeight)
                    {
                        aTrousPongTex?.Dispose();
                        aTrousPongTex = Context!.CreateRWTexture2D<float4>(texWidth, texHeight);
                    }

                    // Build depth and normal texture
                    if (depthNormalsTex == null || depthNormalsTex.Width != texWidth || depthNormalsTex.Height != texHeight || DepthNormalPixels == null)
                    {
                        depthNormalsTex?.Dispose();
                        depthNormalsTex = Context!.CreateRWTexture2D<float4>(texWidth, texHeight);
                        DepthNormalPixels = new float4[texWidth * texHeight];
                    }

                    // Build object ID buffer
                    if (objectIdBuffer == null || objectIdBuffer.Length != (texWidth * texHeight) || ObjectIDs == null)
                    {
                        objectIdBuffer?.Dispose();
                        objectIdBuffer = Context!.CreateRWBuffer<uint2>(texWidth * texHeight);
                        ObjectIDs = new uint2[texWidth * texHeight];
                    }

                    // Build handle ID buffer
                    if (handleIdBuffer == null || handleIdBuffer.Length != (texWidth * texHeight) || HandleIds == null)
                    {
                        handleIdBuffer?.Dispose();
                        handleIdBuffer = Context!.CreateRWBuffer<uint>(texWidth * texHeight);
                        HandleIds = new uint[texWidth * texHeight];
                    }

                    // Build custom shape ID buffer
                    if (customShapeIdBuffer == null || customShapeIdBuffer.Length != (texWidth * texHeight))
                    {
                        customShapeIdBuffer?.Dispose();
                        customShapeIdBuffer = Context!.CreateRWBuffer<uint>(texWidth * texHeight);
                        CustomShapeIds = new uint[texWidth * texHeight];
                    }

                    // Build icon ID buffer
                    if (iconIdBuffer == null || iconIdBuffer.Length != (texWidth * texHeight))
                    {
                        iconIdBuffer?.Dispose();
                        iconIdBuffer = Context!.CreateRWBuffer<uint>(texWidth * texHeight);
                        IconIds = new uint[texWidth * texHeight];
                    }

                    // Build kernel buffer
                    if (kernelBuffer == null || ObjectIDs == null)
                    {
                        kernelBuffer?.Dispose();
                        kernelBuffer = Context!.CreateBuffer<float>([1.0f / 16.0f, 1.0f / 4.0f, 3.0f / 8.0f, 1.0f / 4.0f, 1.0f / 16.0f]);
                    }

                    // Build and copy buffers
                    if (sdfObjBuffer?.Length != sdfObjDTO.Length)
                    {
                        sdfObjBuffer?.Dispose();
                        sdfObjBuffer = Context?.CreateStructuredBuffer(sdfObjDTO);
                    }
                    else sdfObjBuffer.SetData(Context!.Context, sdfObjDTO);
                    if (sdfObjBuffer == null) return;

                    if (lightsBuffer?.Length != sdfLightsDTO.Length)
                    {
                        lightsBuffer?.Dispose();
                        lightsBuffer = Context?.CreateStructuredBuffer(sdfLightsDTO);
                    }
                    else lightsBuffer.SetData(Context!.Context, sdfLightsDTO);
                    if (lightsBuffer == null) return;

                    if (rebuildTextureBuffer && TextureSystem.AllTextureData != null && TextureSystem.AllTextureMetadata != null)
                    {
                        textureBuffer?.Dispose();
                        textureBuffer = Context?.CreateBuffer<uint>(TextureSystem.AllTextureData);
                        if (textureBuffer == null) return;

                        textureMetaBuffer?.Dispose();
                        textureMetaBuffer = Context?.CreateStructuredBuffer(TextureSystem.AllTextureMetadata);
                        if (textureMetaBuffer == null) return;

                        TextureSystem.FreeCPUTextureData();
                        rebuildTextureBuffer = false;
                    }

                    if (rebuildTerrainBuffer && TerrainSystem.AllTerrainData != null && TerrainSystem.AllTerrainMetadata != null)
                    {
                        // Scenes without terrain still need non-empty buffers to bind.
                        float[] heights = TerrainSystem.AllTerrainData is { Length: > 0 } h ? h : new float[1];
                        TerrainDTO[] metas = TerrainSystem.AllTerrainMetadata is { Length: > 0 } m ? m : new TerrainDTO[1];

                        terrainHeightBuffer?.Dispose();
                        terrainHeightBuffer = Context?.CreateBuffer<float>(heights);
                        terrainMetadataBuffer?.Dispose();
                        terrainMetadataBuffer = Context?.CreateStructuredBuffer(metas);
                        rebuildTerrainBuffer = false;
                    }

                    // Checkerboard rendering toggle
                    int checkerboardEnabled = CheckerboardRenderingEnabled ? 1 : 0;
                    int dispatchWidth, dispatchHeight;
                    if (CheckerboardRenderingEnabled)
                    {
                        dispatchWidth = (texWidth + 1) / 2;
                        dispatchHeight = texHeight;
                    }
                    else
                    {
                        dispatchWidth = texWidth;
                        dispatchHeight = texHeight;
                    }

                    // Quickly update the world data buffer
                    SDFRenderSystem.UploadWorldData(Context!);

                    // The voxel path needs non-empty bindings even for scenes without
                    // textures or terrain. The exact path has always relied on these
                    // dummy buffers being created during the terrain rebuild.
                    if (textureBuffer == null) textureBuffer = Context!.CreateBuffer<uint>(new uint[1]);
                    if (textureMetaBuffer == null) textureMetaBuffer = Context!.CreateStructuredBuffer(new TextureMetadata[1]);
                    if (terrainHeightBuffer == null) terrainHeightBuffer = Context!.CreateBuffer<float>(new float[1]);
                    if (terrainMetadataBuffer == null) terrainMetadataBuffer = Context!.CreateStructuredBuffer(new TerrainDTO[1]);

                    bool voxelFrame = VoxelRenderingEnabled;
                    if (voxelFrame)
                    {
                        voxelClipmap ??= new VoxelClipmapRenderer();
                        voxelFrame = voxelClipmap.Update(
                            Context!, worldDTO, sdfObjDTO, sdfObjBuffer,
                            terrainHeightBuffer!, terrainMetadataBuffer!,
                            TerrainSystem.DataVersion, VoxelGridResolution,
                            VoxelUpdateCellBudget);
                    }

                    if (voxelFrame)
                    {
                        DivisionShader voxelShader = GetShader(typeof(VoxelSDFShader3D));
                        voxelShader.SetFloat("width", texWidth);
                        voxelShader.SetFloat("height", texHeight);
                        voxelShader.SetFloat("aspect", texWidth / (float)texHeight);
                        voxelShader.SetInt("frameCount", TimeSystem.FrameCount);
                        voxelShader.SetInt("debugMode", (int)CurrentDebugMode);
                        voxelShader.SetInt("enableCheckerboard", checkerboardEnabled);
                        voxelClipmap!.Bind(voxelShader);
                        voxelShader.SetStructuredBuffer("worldData", SDFRenderSystem.WorldDataBuffer!);
                        voxelShader.SetRWTexture2D("outputTexture", renderTex!);
                        voxelShader.SetRWTexture2D("depthNormals", depthNormalsTex!);
                        voxelShader.SetRWBuffer("entityIdBuffer", objectIdBuffer!);
                        voxelShader.SetStructuredBuffer("sdfObjects", sdfObjBuffer);
                        voxelShader.SetStructuredBuffer("lights", lightsBuffer);
                        voxelShader.SetBuffer("textureData", textureBuffer!);
                        voxelShader.SetStructuredBuffer("textureMetadata", textureMetaBuffer!);
                        voxelShader.SetBuffer("terrainHeightData", terrainHeightBuffer!);
                        voxelShader.SetStructuredBuffer("terrainMetadata", terrainMetadataBuffer!);
                        voxelShader.Dispatch2D(voxelShader.FindKernel("Execute"), dispatchWidth, dispatchHeight);
                    }
                    else
                    {
                        DivisionShader sdfShader = GetShader(typeof(SDFShader3D));
                        sdfShader.SetFloat("width", texWidth);
                        sdfShader.SetFloat("height", texHeight);
                        sdfShader.SetFloat("aspect", texWidth / (float)texHeight);
                        sdfShader.SetInt("frameCount", TimeSystem.FrameCount);
                        sdfShader.SetInt("debugMode", (int)CurrentDebugMode);
                        sdfShader.SetInt("enableCheckerboard", checkerboardEnabled);
                        sdfShader.SetStructuredBuffer("worldData", SDFRenderSystem.WorldDataBuffer!);
                        sdfShader.SetRWTexture2D("outputTexture", renderTex!);
                        sdfShader.SetRWTexture2D("depthNormals", depthNormalsTex!);
                        sdfShader.SetRWBuffer("entityIdBuffer", objectIdBuffer!);
                        sdfShader.SetStructuredBuffer("sdfObjects", sdfObjBuffer);
                        sdfShader.SetStructuredBuffer("lights", lightsBuffer);
                        sdfShader.SetBuffer("textureData", textureBuffer!);
                        sdfShader.SetStructuredBuffer("textureMetadata", textureMetaBuffer!);
                        sdfShader.SetBuffer("terrainHeightData", terrainHeightBuffer!);
                        sdfShader.SetStructuredBuffer("terrainMetadata", terrainMetadataBuffer!);
                        sdfShader.Dispatch2D(sdfShader.FindKernel("Execute"), dispatchWidth, dispatchHeight);
                    }
                }

                RWTexture2D<float4>? currentTexture = renderTex;
                #region postProcessing

                // Pick the camera the frame is post-processed with: the first one that
                // carries PostProcessing settings, falling back to any camera.
                Camera? renderCamera = null;
                PostProcessing? renderPostProcess = null;
                foreach (var (_, _, camera, postProcess) in boundWorld?.QueryData<Transform, Camera, PostProcessing>() ?? [])
                {
                    renderCamera = camera;
                    renderPostProcess = postProcess;
                    break;
                }
                if (renderCamera is null)
                {
                    foreach (var (_, _, camera) in boundWorld?.QueryData<Transform, Camera>() ?? [])
                    {
                        renderCamera = camera;
                        break;
                    }
                }

                // Denoising runs once for the rendered image. It used to run inside a
                // per-camera loop, which meant every extra camera (the editor adds its
                // own camera entity) re-ran the chain over the same ping/pong textures.
                // The second pass then bound one texture as both input and output, which
                // D3D11 resolves by unbinding the shader-resource view: the pass read
                // zeros and the whole scene came out black.
                if (renderCamera is { } postCamera && CurrentDebugMode == DebugMode.None)
                {
                    // Division Denoising
                    if (postCamera.enableDivisionDenoise &&
                        currentTexture != null && postProcessTex != null && aTrousPongTex != null &&
                        objectIdBuffer != null && depthNormalsTex != null)
                    {
                        // Never read and write the same texture in one dispatch.
                        RWTexture2D<float4> denoiseTarget = ReferenceEquals(currentTexture, postProcessTex)
                            ? aTrousPongTex
                            : postProcessTex;

                        lock (SyncLock)
                        {
                            DivisionShader denoiseShader = GetShader(typeof(DivisionDenoiseShader));
                            denoiseShader.SetFloat("width", texWidth);
                            denoiseShader.SetFloat("height", texHeight);
                            denoiseShader.SetFloat("divisionDenoise", postCamera.divisionDenoiseThreshold);
                            denoiseShader.SetInt("divisionDomain", postCamera.divisionDenoiseDomain);
                            denoiseShader.SetRWTexture2D("inputTexture", currentTexture);
                            denoiseShader.SetRWTexture2D("outputTexture", denoiseTarget);
                            denoiseShader.SetRWTexture2D("depthNormals", depthNormalsTex);
                            denoiseShader.SetStructuredBuffer("sdfObjects", sdfObjBuffer);
                            denoiseShader.SetRWBuffer("objectIdBuffer", objectIdBuffer);
                            denoiseShader.Dispatch2D(denoiseShader.FindKernel("Execute"), texWidth, texHeight);
                        }
                        currentTexture = denoiseTarget;
                    }

                    // A-Trous denoising using wavelets
                    if (postCamera.enableATrousDenoise &&
                        currentTexture != null && postProcessTex != null && aTrousPongTex != null && kernelBuffer != null &&
                        objectIdBuffer != null && depthNormalsTex != null)
                    {
                        RWTexture2D<float4> ping = currentTexture;
                        // Ping/pong must be two different textures: reading texels the same
                        // dispatch writes is a UAV race. When the built-in denoiser already
                        // wrote postProcessTex, use the spare target instead.
                        RWTexture2D<float4> pong = ReferenceEquals(ping, postProcessTex)
                            ? aTrousPongTex
                            : postProcessTex;
                        int stepSize = 1;

                        for (int i = 0; i < postCamera.aTrousStepCount; i++)
                        {
                            lock (SyncLock)
                            {
                                DivisionShader aTrousShader = GetShader(typeof(ATrousDenoiseShader));
                                aTrousShader.SetFloat("width", texWidth);
                                aTrousShader.SetFloat("height", texHeight);
                                aTrousShader.SetInt("stepSize", stepSize);
                                aTrousShader.SetRWTexture2D("inputTexture", ping);
                                aTrousShader.SetRWTexture2D("outputTexture", pong);
                                aTrousShader.SetRWTexture2D("depthNormals", depthNormalsTex);
                                aTrousShader.SetStructuredBuffer("sdfObjects", sdfObjBuffer);
                                aTrousShader.SetRWBuffer("objectIdBuffer", objectIdBuffer);
                                aTrousShader.SetBuffer("kernelBuffer", kernelBuffer);
                                aTrousShader.Dispatch2D(aTrousShader.FindKernel("Execute"), texWidth, texHeight);
                            }

                            // Swap buffers for next pass
                            (ping, pong) = (pong, ping);
                            stepSize *= 2;
                        }
                        currentTexture = ping; // Final result
                    }
                }

                // Post-processing: depth of field, FXAA and the colour effects all run
                // on DivisionCompute, in the same order the ComputeSharp pipeline used.
                if (renderCamera is not null && renderPostProcess is not null &&
                    CurrentDebugMode == DebugMode.None && DivisionPostProcess.HasAnyEffect(renderPostProcess, renderCamera))
                {
                    currentTexture = RunDivisionPostProcess(renderPostProcess, renderCamera, texWidth, texHeight, currentTexture, depthNormalsTex);
                }

                #endregion postProcessing
                #region handlesAndIcons

                // Editor handles
                if (editorHandlePosition.HasValue)
                {
                    lock (SyncLock)
                    {
                        DivisionShader handleShader = GetShader(typeof(EditorHandleShader));
                        handleShader.SetFloat("width", texWidth);
                        handleShader.SetFloat("height", texHeight);
                        handleShader.SetFloat("aspect", texWidth / (float)texHeight);
                        handleShader.SetFloat("camScreenDist", worldDTO.camScreenDist);
                        handleShader.SetVector("cameraOrigin", worldDTO.cameraOrigin);
                        handleShader.SetVector("camForward", worldDTO.camForward);
                        handleShader.SetVector("camRight", worldDTO.camRight);
                        handleShader.SetVector("camUp", worldDTO.camUp);
                        handleShader.SetVector("handlePosition", editorHandlePosition.Value);
                        handleShader.SetFloat("handleScale", editorHandleScale);
                        handleShader.SetUInt("hoveredHandle", currentHoveredHandle);
                        handleShader.SetRWTexture2D("renderTexture", currentTexture!);
                        handleShader.SetRWBuffer("handleIdBuffer", handleIdBuffer!);
                        handleShader.Dispatch2D(handleShader.FindKernel("Execute"), texWidth, texHeight);
                    }
                }

                // Custom shapes (bounding boxes, etc.)
                if (customShapes.Count > 0)
                {
                    lock (SyncLock)
                    {
                        foreach (var (shapeId, shape) in customShapes)
                        {
                            DivisionShader shapeShader = GetShader(typeof(CustomShapeShader));
                            shapeShader.SetFloat("width", texWidth);
                            shapeShader.SetFloat("height", texHeight);
                            shapeShader.SetFloat("aspect", texWidth / (float)texHeight);
                            shapeShader.SetFloat("camScreenDist", worldDTO.camScreenDist);
                            shapeShader.SetVector("cameraOrigin", worldDTO.cameraOrigin);
                            shapeShader.SetVector("camForward", worldDTO.camForward);
                            shapeShader.SetVector("camRight", worldDTO.camRight);
                            shapeShader.SetVector("camUp", worldDTO.camUp);
                            shapeShader.SetUInt("shapeId", shapeId);

                            // The shape is a struct, which the packed uniform buffer cannot
                            // carry, so it travels in a one-element structured buffer.
                            customShapeBuffer ??= Context!.CreateStructuredBuffer([shape]);
                            customShapeBuffer.SetData(Context!.Context, [shape]);
                            shapeShader.SetStructuredBuffer("shapeData", customShapeBuffer);
                            shapeShader.SetRWTexture2D("renderTexture", currentTexture!);
                            shapeShader.SetRWBuffer("shapeIdBuffer", customShapeIdBuffer!);
                            shapeShader.Dispatch2D(shapeShader.FindKernel("Execute"), texWidth, texHeight);
                        }
                    }
                }

                // Editor Icons
                if (iconsToRender.Count > 0)
                {
                    lock (SyncLock)
                    {
                        foreach (var (entityId, (position, icon, direction)) in iconsToRender)
                        {
                            DivisionShader iconShader = GetShader(typeof(IconShader));
                            iconShader.SetFloat("width", texWidth);
                            iconShader.SetFloat("height", texHeight);
                            iconShader.SetFloat("aspect", texWidth / (float)texHeight);
                            iconShader.SetFloat("camScreenDist", worldDTO.camScreenDist);
                            iconShader.SetVector("cameraOrigin", worldDTO.cameraOrigin);
                            iconShader.SetVector("camForward", worldDTO.camForward);
                            iconShader.SetVector("camRight", worldDTO.camRight);
                            iconShader.SetVector("camUp", worldDTO.camUp);
                            iconShader.SetVector("iconPosition", position);
                            iconShader.SetUInt("iconType", (uint)icon);
                            iconShader.SetVector("lightDirection", direction);
                            iconShader.SetUInt("entityId", entityId);
                            iconShader.SetRWTexture2D("renderTexture", currentTexture!);
                            iconShader.SetRWBuffer("iconIdBuffer", iconIdBuffer!);
                            iconShader.Dispatch2D(iconShader.FindKernel("Execute"), texWidth, texHeight);
                        }
                    }
                }

                #endregion handlesAndIcons

                // Copy final result for OpenGL display and editor picking
                depthNormalsTex?.GetData(Context!.Context, DepthNormalPixels!);
                objectIdBuffer?.GetData(Context!.Context, ObjectIDs!);
                handleIdBuffer?.GetData(Context!.Context, HandleIds!);
                iconIdBuffer?.GetData(Context!.Context, IconIds!);
                customShapeIdBuffer?.GetData(Context!.Context, CustomShapeIds!);
                currentTexture?.GetData(Context!.Context, Pixels!);
            }
            catch (ObjectDisposedException ex)
            {
                Debug.Warning($"Renderer: Object disposed during rendering", ex);
                CleanupResources();
                return;
            }
            catch (InvalidOperationException ex)
            {
                Debug.Error($"Renderer: Invalid operation during DivisionCompute execution", ex);

                // Drop everything that belonged to the failing device before swapping it in:
                // otherwise the next frame re-binds shaders and textures from the dead device
                // and throws again, which used to create one new device per frame.
                lock (SyncLock)
                {
                    try { Context?.Dispose(); }
                    catch (Exception disposeEx) { Debug.Warning("Renderer: failed to dispose the failing context", disposeEx); }
                    Context = null;
                    CleanupResources(disposeInputContext: false);

                    try
                    {
                        Context = EnsureContext();
                        Debug.Info("Renderer: recreated the DivisionCompute device");
                    }
                    catch (Exception createEx)
                    {
                        Debug.Error("Renderer: could not recreate the DivisionCompute device", createEx);
                        deviceLost = true;
                    }
                }
                return;
            }

            FramesRendered++;

            // Push final texture to OpenGL
            if (presentToGL) PresentToGL(texWidth, texHeight);
        }

        /// <summary>
        /// Runs the DivisionCompute post-processing stage over the current frame and
        /// returns the GPU texture holding the result, so the editor overlays still
        /// draw on top of the processed image.
        /// </summary>
        /// <remarks>
        /// The stage shares the pipeline's device, so the frame never leaves the GPU:
        /// the SDF output texture goes straight in and the processed texture comes
        /// straight back out.
        /// </remarks>
        /// <returns>
        /// The texture holding the processed frame, or <paramref name="currentTexture"/>
        /// unchanged when the stage fails (the error is logged once).
        /// </returns>
        private RWTexture2D<float4>? RunDivisionPostProcess(
            PostProcessing postProcess,
            Camera camera,
            int texWidth,
            int texHeight,
            RWTexture2D<float4>? currentTexture,
            RWTexture2D<float4>? depthNormals)
        {
            if (currentTexture == null) return currentTexture;

            // A failed post stage stays disabled: retrying it every frame would spam
            // the log and drop the frame anyway.
            if (divisionPostUnavailable) return currentTexture;

            try
            {
                divisionPostProcess ??= new DivisionPostProcess(Context!);

                // Depth is only needed for DOF.
                bool needDepth = postProcess.enableDepthOfField && depthNormals != null;

                return divisionPostProcess.Process(
                    currentTexture,
                    needDepth ? depthNormals : null,
                    needDepth,
                    texWidth,
                    texHeight,
                    postProcess,
                    camera);
            }
            catch (Exception ex)
            {
                Debug.Error("Renderer: DivisionCompute post-processing failed; effects are disabled", ex);
                divisionPostUnavailable = true;
                divisionPostProcess?.Dispose();
                divisionPostProcess = null;
                return currentTexture;
            }
        }

        private void ConvertPixelsToBgra(int w, int h)
        {
            if (Pixels == null) return;
            lock (embeddedBufferLock)
            {
                // Guard against Pixels having been reallocated at a different size mid-flight
                if (Pixels.Length != w * h) return;

                int needed = w * h * 4;
                if (embeddedBgraBuffer == null || embeddedBgraBuffer.Length != needed)
                    embeddedBgraBuffer = new byte[needed];

                Parallel.For(0, h, y =>
                {
                    int srcRowStart = (h - 1 - y) * w;
                    int dstRowStart = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        float4 p = Pixels[srcRowStart + x];
                        int o = (dstRowStart + x) * 4;
                        embeddedBgraBuffer[o + 0] = (byte)math.clamp(p.z * 255f, 0, 255);
                        embeddedBgraBuffer[o + 1] = (byte)math.clamp(p.y * 255f, 0, 255);
                        embeddedBgraBuffer[o + 2] = (byte)math.clamp(p.x * 255f, 0, 255);
                        embeddedBgraBuffer[o + 3] = 255;
                    }
                });

                // Record the actual dimensions this buffer holds
                embeddedBufferWidth = w;
                embeddedBufferHeight = h;
            }
        }

        /// <summary>
        /// Blits the latest converted frame into an already-locked, correctly-sized
        /// WriteableBitmap buffer. Returns false if sizes don't match (a resize is pending).
        /// </summary>
        public bool TryCopyEmbeddedFrame(IntPtr destAddress, int destWidth, int destHeight)
        {
            lock (embeddedBufferLock)
            {
                if (embeddedBgraBuffer == null) return false;
                if (destWidth != embeddedBufferWidth || destHeight != embeddedBufferHeight) return false;
                if (embeddedBgraBuffer.Length != destWidth * destHeight * 4) return false; // final belt-and-suspenders check

                Marshal.Copy(embeddedBgraBuffer, 0, destAddress, embeddedBgraBuffer.Length);
                return true;
            }
        }

        #endregion rendering
        #region openGlWindowing

        private void PresentToGL(int texWidth, int texHeight)
        {
            unsafe
            {
                fixed (float4* dataPtr = Pixels)
                {
                    gl!.BindTexture(TextureTarget.Texture2D, glTexture);
                    gl.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        (int)InternalFormat.Rgba32f,
                        (uint)texWidth,
                        (uint)texHeight,
                        0,
                        PixelFormat.Rgba,
                        PixelType.Float,
                        dataPtr);
                }
            }

            gl.Viewport(0, 0, (uint)texWidth, (uint)texHeight);
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear((uint)ClearBufferMask.ColorBufferBit);
            gl.UseProgram(glShaderProgram);

            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, glTexture);
            int loc = gl.GetUniformLocation(glShaderProgram, "tex");
            gl.Uniform1(loc, 0);

            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            if (!presentLogged && presentLogEnabled)
            {
                presentLogged = true;
                unsafe
                {
                    float* px = stackalloc float[4];
                    gl.ReadPixels(texWidth / 2, texHeight / 2, 1, 1, PixelFormat.Rgba, PixelType.Float, px);
                    float4 cpu = Pixels![(texHeight / 2) * texWidth + texWidth / 2];
                    Debug.Info($"DBG present: window={RendererWindow?.Size.X}x{RendererWindow?.Size.Y} " +
                               $"render={texWidth}x{texHeight} glCenter=({px[0]:F3},{px[1]:F3},{px[2]:F3}) " +
                               $"cpuCenter=({cpu.x:F3},{cpu.y:F3},{cpu.z:F3}) checkerboard={CheckerboardRenderingEnabled}");
                }
            }

            gl.Finish();
        }

        /// <summary>
        /// Compiles and links vertex and fragment shaders into a shader program.
        /// </summary>
        /// <returns>The handle of the compiled and linked shader program.</returns>
        private uint CompileShaders()
        {
            string vertexSrc = """
            #version 330 core
            out vec2 uv;

            void main() {
                const vec2 positions[4] = vec2[](
                    vec2(-1, -1),
                    vec2( 1, -1),
                    vec2(-1,  1),
                    vec2( 1,  1)
                );
                const vec2 uvs[4] = vec2[](
                    vec2(0, 0),
                    vec2(1, 0),
                    vec2(0, 1),
                    vec2(1, 1)
                );
                gl_Position = vec4(positions[gl_VertexID], 0, 1);
                uv = uvs[gl_VertexID];
            }
            """;

            string fragSrc = """
            #version 330 core
            in vec2 uv;
            out vec4 fragColor;
            uniform sampler2D tex;

            void main() {
                fragColor = texture(tex, uv);
            }
            """;

            uint vs = gl!.CreateShader(ShaderType.VertexShader);
            gl.ShaderSource(vs, vertexSrc);
            gl.CompileShader(vs);
            CheckShaderCompileStatus(vs, "Vertex Shader");

            uint fs = gl.CreateShader(ShaderType.FragmentShader);
            gl.ShaderSource(fs, fragSrc);
            gl.CompileShader(fs);
            CheckShaderCompileStatus(fs, "Fragment Shader");

            uint shader = gl.CreateProgram();
            gl.AttachShader(shader, vs);
            gl.AttachShader(shader, fs);
            gl.LinkProgram(shader);
            CheckProgramLinkStatus(shader);

            gl.DeleteShader(vs);
            gl.DeleteShader(fs);
            return shader;
        }

        /// <summary>
        /// Checks the compile status of a shader and debugs the result.
        /// </summary>
        /// <remarks>If the shader compilation fails, the method retrieves the error log and writes it to
        /// the debug output. If the compilation succeeds, a success message is written to the debug output.</remarks>
        /// <param name="shader">The identifier of the shader to check.</param>
        /// <param name="name">The name of the shader, used for logging purposes.</param>
        private void CheckShaderCompileStatus(uint shader, string name)
        {
            gl!.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
            if (success == 0)
            {
                string info = gl.GetShaderInfoLog(shader);
                Debug.Error($"{name} Compile Error: {info}");
            }
            else Debug.Info($"{name} Compiled Successfully");
        }

        /// <summary>
        /// Checks the link status of the OpenGL shader program and debugs the result.
        /// </summary>
        /// <remarks>If the shader program fails to link, the method retrieves and debugs the program's
        /// error information. If the shader program links successfully, a success message is displayed.</remarks>
        /// <param name="program">The identifier of the shader program to check.</param>
        private void CheckProgramLinkStatus(uint program)
        {
            gl!.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
            if (success == 0)
            {
                string info = gl.GetProgramInfoLog(program);
                Debug.Error($"Shader Program Link Error: {info}");
            }
            else Debug.Info("Shader Program Linked Successfully");
        }

        #endregion openGLWindowing

        /// <summary>
        /// Disposes of all managed memory and sets to null.
        /// </summary>
        /// <param name="disposeInputContext">
        /// Whether the window's input context should be disposed too. It belongs to the window,
        /// not to the compute device, so device recovery must leave it alone.
        /// </param>
        private void CleanupResources(bool disposeInputContext = true)
        {
            renderTex?.Dispose();
            postProcessTex?.Dispose();
            aTrousPongTex?.Dispose();
            depthNormalsTex?.Dispose();
            lowResDepthTex?.Dispose();
            lowResRenderTex?.Dispose();
            objectIdBuffer?.Dispose();
            handleIdBuffer?.Dispose();
            iconIdBuffer?.Dispose();
            customShapeIdBuffer?.Dispose();
            sdfObjBuffer?.Dispose();
            lightsBuffer?.Dispose();
            kernelBuffer?.Dispose();
            textureBuffer?.Dispose();
            textureMetaBuffer?.Dispose();
            terrainHeightBuffer?.Dispose();
            terrainMetadataBuffer?.Dispose();
            voxelClipmap?.Dispose();
            voxelClipmap = null;
            if (disposeInputContext)
            {
                inputContext?.Dispose(); // Rare null reference error here
                inputContext = null;
            }
            fxaaInputTex?.Dispose();
            customShapeBuffer?.Dispose();
            divisionPostProcess?.Dispose();

            foreach (DivisionShader shader in shaders.Values)
                shader.Dispose();
            shaders.Clear();

            renderTex = null;
            postProcessTex = null;
            aTrousPongTex = null;
            depthNormalsTex = null;
            lowResDepthTex = null;
            lowResRenderTex = null;
            objectIdBuffer = null;
            handleIdBuffer = null;
            iconIdBuffer = null;
            customShapeIdBuffer = null;
            sdfObjBuffer = null;
            lightsBuffer = null;
            kernelBuffer = null;
            textureBuffer = null;
            textureMetaBuffer = null;
            terrainHeightBuffer = null;
            terrainMetadataBuffer = null;
            fxaaInputTex = null;
            customShapeBuffer = null;
            divisionPostProcess = null;

            // Force the CPU-side upload caches to be rebuilt against the new device.
            rebuildTextureBuffer = true;
            rebuildTerrainBuffer = true;
        }
    }
}


