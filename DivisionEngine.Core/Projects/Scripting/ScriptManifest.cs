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
    /// Describes the set of scripts that belong to a project, split into the
    /// player assembly and the editor assembly. Holds loaded
    /// <see cref="ScriptAsset"/> instances so the compiler reads source from the
    /// same objects the asset browser displays.
    /// </summary>
    public sealed class ScriptManifest(
        string projectName,
        string projectDirectory,
        IReadOnlyList<ScriptAsset> playerScripts,
        IReadOnlyList<ScriptAsset> editorScripts,
        string engineAssemblyPath)
    {
        public string ProjectName { get; } = projectName;
        public string ProjectDirectory { get; } = projectDirectory;

        /// <summary>
        /// Loaded script assets targeting the player assembly.
        /// </summary>
        public IReadOnlyList<ScriptAsset> PlayerScripts { get; } = playerScripts;

        /// <summary>
        /// Loaded script assets targeting the editor assembly.
        /// </summary>
        public IReadOnlyList<ScriptAsset> EditorScripts { get; } = editorScripts;

        public string PlayerAssemblyName { get; } = $"{projectName}.Player";
        public string EditorAssemblyName { get; } = $"{projectName}.Editor";
        public string EngineAssemblyPath { get; } = engineAssemblyPath;
        public DateTime BuiltAtUtc { get; } = DateTime.UtcNow;

        public int TotalScriptCount => PlayerScripts.Count + EditorScripts.Count;
        public bool HasScripts => TotalScriptCount > 0;

        /// <summary>
        /// Builds a manifest by loading every script asset registered in the
        /// current <see cref="AssetDatabase"/> through <see cref="AssetManager"/>.
        /// Returns null if no project is loaded or the asset manager is missing.
        /// </summary>
        public static async Task<ScriptManifest?> BuildFromProjectAsync(
            string projName, string projDir)
        {
            AssetManager? manager = ProjectManager.AssetManager;
            if (manager == null) return null;

            List<ScriptAsset> playerScripts = [];
            List<ScriptAsset> editorScripts = [];

            foreach (AssetMetadata meta in AssetDatabase.GetAssetsByType(AssetType.Script))
            {
                // Skip tooling artifacts hidden in dot-folders
                if (meta.RelativePath.Contains("\\.") || meta.RelativePath.Contains("/."))
                    continue;

                ScriptAsset? script = await manager.LoadAssetAsync<ScriptAsset>(meta.ID);
                if (script == null) continue;

                // Force a freshness check. LoadAssetAsync returns the cached instance if
                // the script is already loaded, which means edits made after the first
                // load never reach the compiler. ReloadIfChangedAsync compares file mtime
                // against the last-load time and re-reads the source if needed. No-op
                // when the file hasn't changed
                await script.ReloadIfChangedAsync();
                if (!script.IsLoaded) continue;

                if (script.CompileTarget == ScriptCompileTarget.Editor)
                    editorScripts.Add(script);
                else
                    playerScripts.Add(script);
            }

            // Deterministic ordering for easier diffing/debugging
            playerScripts.Sort((a, b) => string.Compare(
                a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            editorScripts.Sort((a, b) => string.Compare(
                a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));

            string engineAssembly = typeof(ProjectManager).Assembly.Location;

            return new ScriptManifest(projName, projDir,
                playerScripts, editorScripts, engineAssembly);
        }
    }
}
