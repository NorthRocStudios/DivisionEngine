//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia.Threading;
using DivisionEngine.Editor.Tasks;
using Material.Icons;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Recompiles every shader from source and swaps the result into the renderer.
    /// Backs the Edit menu's "Reload All Shaders" command.
    /// </summary>
    /// <remarks>
    /// Compilation runs on background threads and is surfaced through the editor task
    /// list. The renderer's compiled shaders are only dropped once the recompile has
    /// finished, so a failed shader keeps rendering with its previous bytecode.
    /// </remarks>
    internal static class ShaderReload
    {
        private static bool running;

        /// <summary>
        /// True while a reload is in progress.
        /// </summary>
        public static bool IsRunning => running;

        /// <summary>
        /// Starts recompiling every shader. Ignored while a reload is already running.
        /// </summary>
        public static void Run()
        {
            if (running)
            {
                Debug.Info("Shader reload: already in progress");
                return;
            }

            running = true;

            EditorTask task = EditorTaskManager.Create(
                "Shader Reload",
                "Reloading shaders",
                0f,
                MaterialIconKind.Texture);

            Task.Run(() => Worker(task));
        }

        private static void Worker(EditorTask task)
        {
            int total = 0;
            int completed = 0;
            int failed = 0;

            try
            {
                List<Type> shaders = ShaderDiscovery.Discover();
                total = shaders.Count;

                // Cache-aware: a shader whose source changed is compiled here (in the
                // background) and its cache entry is refreshed, so the swap below is a
                // cache hit even for the expensive raymarching shader. Unchanged shaders
                // reuse their cached bytecode.
                int workers = Math.Min(4, Math.Max(1, Environment.ProcessorCount - 2));
                Parallel.ForEach(shaders, new ParallelOptions { MaxDegreeOfParallelism = workers }, shader =>
                {
                    try
                    {
                        DivisionShaderCompiler compiler = new();
                        HLSLTranslationResult translation = compiler.TranslateShader(shader);
                        if (!translation.IsSuccess || !DivisionShaderCompiler.CompileHLSL(translation).IsSuccess)
                            Interlocked.Increment(ref failed);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        Debug.Warning($"Shader reload: '{shader.Name}' failed", ex);
                    }

                    int done = Interlocked.Increment(ref completed);
                    ReportProgress(task, done, total);
                });

                // Drop the old compiled shaders only now: the render thread picks the new
                // bytecode up on its next frame (cache hits, so no visible stall).
                Dispatcher.UIThread.Post(() =>
                {
                    App.Renderer?.ReloadShaders();
                    EditorTaskManager.Complete(task.Id);
                    EditorTaskManager.Remove(task.Id);
                    Debug.Info($"Shader reload: {completed}/{total} shader(s) reloaded" +
                        (failed > 0 ? $", {failed} failed" : string.Empty));
                    running = false;
                });
            }
            catch (Exception ex)
            {
                Debug.Error("Shader reload failed", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    EditorTaskManager.Complete(task.Id);
                    EditorTaskManager.Remove(task.Id);
                    running = false;
                });
            }
        }

        private static void ReportProgress(EditorTask task, int done, int total)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    task.Description = $"Reloading {done}/{total}";
                    EditorTaskManager.Update(task.Id, total > 0 ? (float)done / total : 0f);
                });
            }
            catch
            {
                // The editor is shutting down; nothing left to report to.
            }
        }
    }
}
