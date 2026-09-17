//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Projects.Scripting
{
    /// <summary>
    /// A no-op compiler that logs what it *would* compile. Used until the
    /// Roslyn backend is wired in, so the pipeline can be exercised end-to-end
    /// without any compiler dependency.
    /// </summary>
    internal sealed class NullScriptCompiler : IScriptCompiler
    {
        public string Name => "Null (no compiler registered)";

        public ScriptCompileResult Compile(ScriptManifest manifest)
        {
            string message =
                $"Null compiler: would compile {manifest.PlayerScripts.Count} player " +
                $"and {manifest.EditorScripts.Count} editor script(s) into " +
                $"'{manifest.PlayerAssemblyName}' / '{manifest.EditorAssemblyName}'.";

            Debug.Info(message);
            return ScriptCompileResult.Empty(message);
        }
    }
}
