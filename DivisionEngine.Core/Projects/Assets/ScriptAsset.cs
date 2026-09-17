//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Text;

namespace DivisionEngine.Projects.Assets
{
    /// <summary>
    /// Which compiled assembly a script belongs to.
    /// </summary>
    public enum ScriptCompileTarget
    {
        /// <summary>
        /// Scripts compiled into the project's player assembly.
        /// </summary>
        Player = 0,
        /// <summary>
        /// Scripts compiled into the project's editor assembly (inside an "Editor" folder).
        /// </summary>
        Editor = 1,
    }

    [AssetType(AssetType.Script)]
    public class ScriptAsset(AssetMetadata metadata) : Asset(metadata)
    {
        private string? sourceCode;
        private DateTime lastLoadedTime;

        /// <summary>Gets the source code of the script, or null if not loaded.</summary>
        public string? SourceCode => sourceCode;

        /// <summary>Gets the absolute file path to the script.</summary>
        public string FullPath => Path.Combine(AssetDatabase.ProjectPath, RelativePath);

        /// <summary>
        /// Which compiled assembly this script belongs to. Determined by whether
        /// the script lives inside an "Editor" folder at any depth, matching the
        /// convention the IDE scaffolder encodes in its compile-item globs.
        /// </summary>
        public ScriptCompileTarget CompileTarget => IsEditorScript(RelativePath)
            ? ScriptCompileTarget.Editor
            : ScriptCompileTarget.Player;

        /// <summary>Last write time observed at the moment the script was loaded.</summary>
        public DateTime LastLoadedTimeUtc => lastLoadedTime;

        public override async Task<bool> LoadAsync()
        {
            if (IsLoaded) return true;

            try
            {
                sourceCode = await File.ReadAllTextAsync(FullPath, Encoding.UTF8);
                lastLoadedTime = File.GetLastWriteTimeUtc(FullPath);

                IsLoaded = true;
                Debug.Info($"Script loaded: {Metadata.FileName} ({sourceCode.Length} characters)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load script {Metadata.FileName}: {ex.Message}");
                IsLoaded = false;
                sourceCode = null;
                return false;
            }
        }

        /// <summary>
        /// Reloads the script if it has changed on disk.
        /// </summary>
        public async Task<bool> ReloadIfChangedAsync()
        {
            if (!IsLoaded) return await LoadAsync();

            DateTime lastWriteTime = File.GetLastWriteTimeUtc(FullPath);
            if (lastWriteTime > lastLoadedTime)
            {
                Debug.Info($"Script changed on disk, reloading: {Metadata.FileName}");
                Unload();
                return await LoadAsync();
            }

            return true;
        }

        public override void Unload()
        {
            if (!IsLoaded) return;

            sourceCode = null;
            IsLoaded = false;

            Debug.Info($"Script unloaded: {Metadata.FileName}");
        }

        public override string ToString() => sourceCode ?? string.Empty;

        /// <summary>
        /// Returns true if the relative path contains an "Editor" folder segment
        /// at any depth. This is the single authoritative definition of the
        /// convention; the IDE scaffolder's globs mirror it, and if the
        /// convention ever changes, it changes here.
        /// </summary>
        private static bool IsEditorScript(string relativePath)
        {
            string normalized = "\\" + relativePath.Replace('/', '\\') + "\\";
            return normalized.Contains(@"\Editor\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
