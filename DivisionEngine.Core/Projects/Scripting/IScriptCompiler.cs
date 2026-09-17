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
    /// A pluggable backend that compiles a <see cref="ScriptManifest"/> into
    /// one or more assemblies.
    /// <para>
    /// Implementations receive already-loaded <see cref="Assets.ScriptAsset"/>
    /// instances, so the source text is available via <c>SourceCode</c> and no
    /// file I/O is required on the compiler's part.
    /// </para>
    /// </summary>
    public interface IScriptCompiler
    {
        /// <summary>
        /// Human-readable name used in log output.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Compiles the scripts described by <paramref name="manifest"/>.
        /// Must not throw - surface all failures via <see cref="ScriptCompileResult"/>.
        /// </summary>
        ScriptCompileResult Compile(ScriptManifest manifest);
    }
}
