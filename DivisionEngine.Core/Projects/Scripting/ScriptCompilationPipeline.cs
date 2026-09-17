//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Projects.Assets;

namespace DivisionEngine.Projects.Scripting
{
    /// <summary>
    /// Orchestrates the script compilation lifecycle: discovers scripts through
    /// the <see cref="AssetManager"/>, builds a <see cref="ScriptManifest"/>,
    /// invokes the registered <see cref="IScriptCompiler"/>, and loads the
    /// resulting assemblies into a collectible <see cref="ScriptLoadContext"/>.
    /// </summary>
    public static class ScriptCompilationPipeline
    {
        private const int RecompileDebounceMs = 500;

        private static IScriptCompiler? compiler;
        private static ScriptManifest? currentManifest;
        private static ScriptCompileResult? lastResult;
        private static ScriptLoadContext? currentLoadContext;
        private static Timer? debounceTimer;
        private static bool isDirty;
        private static bool isInitialized;
        private static bool isCompiling;
        private static readonly Lock gate = new();

        /// <summary>
        /// Raised when a new manifest has been built.
        /// </summary>
        public static event Action<ScriptManifest>? ManifestBuilt;

        /// <summary>
        /// Raised at the start of a compile pass, before the manifest is built.
        /// </summary>
        public static event Action? CompilationStarted;

        /// <summary>
        /// Raised when a compilation pass completes (success or failure).
        /// </summary>
        public static event Action<ScriptCompileResult>? CompilationCompleted;

        /// <summary>
        /// Raised after a successful compile when new assemblies have been
        /// loaded into <see cref="CurrentLoadContext"/>. Subscribers can use
        /// this to react to new script types (rebuild menus, re-register
        /// systems, etc.).
        /// </summary>
        public static event Action<ScriptLoadContext>? ScriptsLoaded;

        public static ScriptManifest? CurrentManifest => currentManifest;
        public static ScriptCompileResult? LastResult => lastResult;
        public static ScriptLoadContext? CurrentLoadContext => currentLoadContext;
        public static IScriptCompiler Compiler => compiler ??= new NullScriptCompiler();

        /// <summary>
        /// Installs a compiler backend. Call before <see cref="Initialize"/> if
        /// you want it used on the initial compile.
        /// </summary>
        public static void SetCompiler(IScriptCompiler newCompiler)
        {
            compiler = newCompiler;
            Debug.Info($"Script Pipeline: compiler set to '{newCompiler.Name}'");
        }

        public static void Initialize()
        {
            if (isInitialized) return;
            AssetDatabase.AssetsUpdated += OnAssetsUpdated;
            AssetDatabase.FolderChanged += OnFolderChanged;
            debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
            isInitialized = true;
        }

        public static void Shutdown()
        {
            if (!isInitialized) return;
            AssetDatabase.AssetsUpdated -= OnAssetsUpdated;
            AssetDatabase.FolderChanged -= OnFolderChanged;
            debounceTimer?.Dispose();
            debounceTimer = null;

            if (currentLoadContext != null)
            {
                try { currentLoadContext.Unload(); }
                catch (Exception ex)
                {
                    Debug.Warning("Script Pipeline: unload on shutdown failed", ex);
                }
                currentLoadContext = null;
            }

            currentManifest = null;
            lastResult = null;
            isDirty = false;
            isInitialized = false;
        }

        public static async Task<ScriptManifest?> BuildManifestAsync()
        {
            if (!ProjectManager.IsCurrentLoaded)
            {
                Debug.Warning("Script Pipeline: cannot build manifest - no project loaded.");
                return null;
            }

            currentManifest = await ScriptManifest.BuildFromProjectAsync(
                ProjectManager.CurrentProjectName!,
                ProjectManager.CurrentProjectPath!);
            if (currentManifest == null) return null;

            Debug.Info($"Script Pipeline: manifest built - {currentManifest.PlayerScripts.Count} player, " +
                       $"{currentManifest.EditorScripts.Count} editor script(s)");
            ManifestBuilt?.Invoke(currentManifest);
            return currentManifest;
        }

        public static async Task<ScriptCompileResult> CompileAsync()
        {
            lock (gate)
            {
                if (isCompiling)
                {
                    Debug.Info("Script Pipeline: compile already in progress; skipping.");
                    return lastResult ?? ScriptCompileResult.Empty("compile already running");
                }
                isCompiling = true;
            }

            try
            {
                if (currentManifest == null) await BuildManifestAsync();
                if (currentManifest == null) return ScriptCompileResult.Empty("no manifest");

                isDirty = false;
                Debug.Info($"Script Pipeline: compiling with '{Compiler.Name}'");

                ScriptCompileResult result;
                try
                {
                    result = Compiler.Compile(currentManifest);
                }
                catch (Exception ex)
                {
                    Debug.Error("Script Pipeline: compiler threw unexpectedly", ex);
                    result = new ScriptCompileResult(
                        false,
                        [new ScriptDiagnostic(ScriptDiagnosticSeverity.Error,
                            $"Compiler threw: {ex.Message}")],
                        TimeSpan.Zero);
                }

                lastResult = result;

                if (result.Success && result.PlayerAssembly != null)
                {
                    Debug.Info($"Script Pipeline: compilation succeeded in {result.Duration.TotalMilliseconds:F0}ms");
                    LoadCompiledAssemblies(result);
                }
                else if (result.Success)
                    Debug.Warning("Script Pipeline: compiler reported success but produced no player assembly.");
                else Debug.Error($"Script Pipeline: compilation failed with {result.Diagnostics.Count} diagnostic(s)");

                CompilationCompleted?.Invoke(result);
                return result;
            }
            finally
            {
                lock (gate) isCompiling = false;
            }
        }

        public static async Task<ScriptCompileResult> RefreshAndCompileAsync()
        {
            CompilationStarted?.Invoke();
            await BuildManifestAsync();
            return await CompileAsync();
        }

        public static void MarkDirty()
        {
            lock (gate)
            {
                isDirty = true;
                debounceTimer?.Change(RecompileDebounceMs, Timeout.Infinite);
            }
        }

		#region assemblyLoading

		private static void LoadCompiledAssemblies(ScriptCompileResult result)
        {
            // Drop the previous context. Unload() is asynchronous - the runtime
            // finalizes once all references to its assemblies are released.
            if (currentLoadContext != null)
            {
                Debug.Info("Script Pipeline: unloading previous script load context");
                try { currentLoadContext.Unload(); }
                catch (Exception ex)
                {
                    Debug.Warning("Script Pipeline: previous context unload failed", ex);
                }
                currentLoadContext = null;
            }

            ScriptLoadContext newContext = new();
            try
            {
                newContext.LoadCompiled(result.PlayerAssembly!);
                if (result.EditorAssembly != null) newContext.LoadCompiled(result.EditorAssembly);
            }
            catch (Exception ex)
            {
                Debug.Error("Script Pipeline: failed to load compiled assemblies", ex);
                try { newContext.Unload(); } catch { /* best-effort */ }
                return;
            }

            currentLoadContext = newContext;

            // New assemblies are now loaded - anything that wants to reference
            // them next compile needs a fresh reference cache
            RoslynScriptCompiler.InvalidateReferenceCache();

            Debug.Info($"Script Pipeline: loaded {newContext.LoadedAssemblies.Count} " +
                       $"script assembl{(newContext.LoadedAssemblies.Count == 1 ? "y" : "ies")}");

            ScriptsLoaded?.Invoke(newContext);
        }

		#endregion
		#region eventHandlers

		private static void OnAssetsUpdated() => MarkDirty();
        private static void OnFolderChanged(string _) => MarkDirty();

        private static async void OnDebounceElapsed(object? state)
        {
            lock (gate)
            {
                if (!isDirty || !isInitialized) return;
                if (!ProjectManager.IsCurrentLoaded) return;
                isDirty = false;
            }
            try
            {
                await RefreshAndCompileAsync();
            }
            catch (Exception ex)
            {
                Debug.Error("Script Pipeline: auto-recompile failed", ex);
            }
        }

		#endregion
	}
}
