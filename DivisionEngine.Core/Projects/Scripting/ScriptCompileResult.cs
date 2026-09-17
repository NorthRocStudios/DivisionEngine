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
    /// A compiled assembly image plus its optional PDB, ready to be loaded into
    /// an <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
    /// </summary>
    public sealed class CompiledAssembly(string name, byte[] image, byte[]? pdb)
    {
        /// <summary>
        /// Simple assembly name (e.g. "MyGame.Player").
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Raw PE image bytes of the compiled assembly.
        /// </summary>
        public byte[] Image { get; } = image;

        /// <summary>
        /// Raw portable-PDB bytes, or null if no symbols were emitted.
        /// </summary>
        public byte[]? Pdb { get; } = pdb;
    }

    /// <summary>
    /// The outcome of a script compilation pass: diagnostics, timing, and the
    /// compiled assemblies (if the pass succeeded).
    /// </summary>
    public sealed class ScriptCompileResult(
        bool success,
        IReadOnlyList<ScriptDiagnostic> diagnostics,
        TimeSpan duration,
        CompiledAssembly? playerAssembly = null,
        CompiledAssembly? editorAssembly = null)
    {
        public bool Success { get; } = success;
        public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; } = diagnostics;
        public TimeSpan Duration { get; } = duration;
        public CompiledAssembly? PlayerAssembly { get; } = playerAssembly;
        public CompiledAssembly? EditorAssembly { get; } = editorAssembly;

        public static ScriptCompileResult Empty(string infoMessage) =>
            new(true,
                [new ScriptDiagnostic(ScriptDiagnosticSeverity.Info, infoMessage)],
                TimeSpan.Zero);
    }
}
