//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Editor.Settings;
using DivisionEngine.Editor.Tasks;
using DivisionEngine.Rendering;
using Material.Icons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Precompiles every <c>[Shader]</c> struct into the DivisionCompute shader cache
    /// on a background thread, so later launches start without compiling anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The preload starts once the renderer has produced its first frame. The renderer
    /// compiles the shaders it needs synchronously on that frame; preloading them again
    /// would duplicate that work, while a cache hit afterwards costs nothing.
    /// </para>
    /// <para>
    /// Progress is surfaced through <see cref="EditorTaskManager"/> (the editor's
    /// background task list). All task mutations happen on the editor update thread;
    /// the worker only touches counters guarded by <see cref="sync"/>.
    /// </para>
    /// </remarks>
    internal class ShaderPreloadSystem : SystemBase
    {
        private static readonly object sync = new();

        private static EditorTask? preloadTask;
        private static bool started;
        private static bool workerStarted;
        private static int total;
        private static int completed;
        private static int lastReported = -1;
        private static string currentShader = string.Empty;
        private static bool finished;
        private static volatile bool cancelled;

        public override void EditorUpdate()
        {
            if (!started)
            {
                started = true;
                if (!EditorSettings.Instance.PrecompileShadersInBackground ||
                    !EditorSettings.Instance.ShaderCacheEnabled)
                    return;

                preloadTask = EditorTaskManager.Create(
                    "Shader Preload",
                    "Waiting for the first frame",
                    0f,
                    MaterialIconKind.Texture);
                return;
            }

            if (preloadTask is null) return;

            // Start the worker only once a frame has finished: the renderer compiles the
            // shaders it needs synchronously on the first frame, and preloading the same
            // list beforehand would duplicate that work instead of reusing the cache.
            if (!workerStarted)
            {
                if (RenderPipeline.FramesRendered < 1) return;
                workerStarted = true;
                preloadTask.Description = "Precompiling shaders into the shader cache";
                Task.Run(PreloadShaders);
                return;
            }

            int done, count;
            string shaderName;
            bool doneAll;
            lock (sync)
            {
                done = completed;
                count = total;
                shaderName = currentShader;
                doneAll = finished;
            }

            if (doneAll || count == 0)
            {
                EditorTaskManager.Complete(preloadTask.Id);
                EditorTaskManager.Remove(preloadTask.Id);
                LogCompletion();
                preloadTask = null;
                return;
            }

            // Only touch the task list when something actually changed; the update
            // fires TasksChanged, which rebuilds the UI's task list.
            if (done != lastReported)
            {
                lastReported = done;
                if (!string.IsNullOrEmpty(shaderName))
                    preloadTask.Description = $"Precompiling {shaderName}";
                EditorTaskManager.Update(preloadTask.Id, (float)done / count);
            }
        }

        private static void LogCompletion()
        {
            int count;
            lock (sync) count = total;
            Debug.Info($"Shader preload: {count} shader(s) available in the cache");
        }

        /// <summary>
        /// Stops the worker on world unload. A compile already running finishes, but the
        /// remaining shaders are skipped.
        /// </summary>
        public override void Unload() => cancelled = true;

        private static void PreloadShaders()
        {
            try
            {
                // The renderer compiles the shaders it needs on the first frame; those are
                // skipped so the preload does not translate them a second time.
                List<Type> shaders = ShaderDiscovery.Discover()
                    .Where(type => !DivisionShaderCompiler.IsShaderCompiled(type.Name))
                    .ToList();
                lock (sync) total = shaders.Count;

                // Shaders are independent; a few workers keep the editor responsive while
                // the remaining ones are translated and compiled. D3DCompile itself is
                // single-threaded per kernel.
                int workers = Math.Min(4, Math.Max(1, Environment.ProcessorCount - 2));
                Parallel.ForEach(shaders, new ParallelOptions { MaxDegreeOfParallelism = workers }, shader =>
                {
                    if (cancelled) return;

                    lock (sync) currentShader = shader.Name;
                    try
                    {
                        DivisionShaderCompiler compiler = new();
                        HLSLTranslationResult translation = compiler.TranslateShader(shader);
                        if (translation.IsSuccess)
                            DivisionShaderCompiler.CompileHLSL(translation);
                        else
                            Debug.Warning($"Shader preload: '{shader.Name}' failed to translate: {translation.ErrorMessage}");
                    }
                    catch (Exception ex)
                    {
                        Debug.Warning($"Shader preload: '{shader.Name}' failed", ex);
                    }

                    lock (sync) completed++;
                    if (cancelled) return;
                });
            }
            catch (Exception ex)
            {
                Debug.Warning("Shader preload: failed to enumerate shaders", ex);
            }
            finally
            {
                lock (sync) finished = true;
            }
        }   }
}
