//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DivisionEngine.Projects.Scripting
{
    /// <summary>
    /// Compiles a <see cref="ScriptManifest"/> using Roslyn. Produces two
    /// assemblies - player and editor - as raw PE images.
    /// </summary>
    public sealed class RoslynScriptCompiler : IScriptCompiler
    {
        public string Name => "Roslyn";

        public ScriptCompileResult Compile(ScriptManifest manifest)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<ScriptDiagnostic> diagnostics = [];

            try
            {
                // Player assembly first
                CompilationResult playerCompile = CompileOneAssembly(
                    assemblyName: manifest.PlayerAssemblyName,
                    scripts: manifest.PlayerScripts,
                    additionalReferences: [],
                    diagnosticsSink: diagnostics);

                if (!playerCompile.Success)
                {
                    sw.Stop();
                    return new ScriptCompileResult(false, diagnostics, sw.Elapsed);
                }

                // Editor assembly with the player image as a reference

                // Passing the player's emitted image directly gives the editor
                // compilation a view of exactly what the player will expose at
                // runtime, without needing to load the player first.
                MetadataReference playerRef = MetadataReference.CreateFromImage(
                    playerCompile.Image!);

                CompilationResult editorCompile = CompileOneAssembly(
                    assemblyName: manifest.EditorAssemblyName,
                    scripts: manifest.EditorScripts,
                    additionalReferences: [playerRef],
                    diagnosticsSink: diagnostics);

                sw.Stop();

                CompiledAssembly playerAssembly = new(
                    manifest.PlayerAssemblyName,
                    playerCompile.Image!,
                    playerCompile.Pdb);

                CompiledAssembly? editorAssembly = editorCompile.Image != null
                    ? new CompiledAssembly(manifest.EditorAssemblyName,
                                           editorCompile.Image,
                                           editorCompile.Pdb)
                    : null;

                return new ScriptCompileResult(
                    editorCompile.Success,
                    diagnostics,
                    sw.Elapsed,
                    playerAssembly,
                    editorAssembly);
            }
            catch (Exception ex)
            {
                sw.Stop();
                diagnostics.Add(new ScriptDiagnostic(
                    ScriptDiagnosticSeverity.Error,
                    $"Roslyn compiler threw: {ex.Message}"));
                Debug.Error("Roslyn compiler failed", ex);
                return new ScriptCompileResult(false, diagnostics, sw.Elapsed);
            }
        }

		#region Single-assembly compilation

		private readonly record struct CompilationResult(
            bool Success, byte[]? Image, byte[]? Pdb);

        private static CompilationResult CompileOneAssembly(
            string assemblyName,
            IReadOnlyList<Assets.ScriptAsset> scripts,
            IReadOnlyList<MetadataReference> additionalReferences,
            List<ScriptDiagnostic> diagnosticsSink)
        {
            List<SyntaxTree> trees = [];
            CSharpParseOptions parseOptions = new(
                LanguageVersion.Latest,
                DocumentationMode.None,
                SourceCodeKind.Regular);

            foreach (Assets.ScriptAsset script in scripts)
            {
                string source = script.SourceCode ?? string.Empty;

                // Wrap in a SourceText with an explicit encoding. Without this,
                // Roslyn refuses to emit a PDB with:
                //   "Cannot emit debug information for a source text without encoding."
                // UTF-8 matches what ScriptAsset.LoadAsync uses when reading from disk
                SourceText sourceText = SourceText.From(source, Encoding.UTF8);

                trees.Add(CSharpSyntaxTree.ParseText(
                    sourceText, parseOptions, path: script.FullPath));
            }

            List<MetadataReference> references = [.. GetOrBuildBaseReferences()];
            references.AddRange(additionalReferences);

            CSharpCompilationOptions compilationOptions = new(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable,
                platform: Platform.AnyCpu,
                deterministic: true);

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName, trees, references, compilationOptions);

            using MemoryStream peStream = new();
            using MemoryStream pdbStream = new();

            EmitResult emitResult = compilation.Emit(
                peStream, pdbStream,
                options: new EmitOptions(
                    debugInformationFormat: DebugInformationFormat.PortablePdb));

            bool hadErrors = false;
            foreach (Diagnostic diag in emitResult.Diagnostics)
            {
                if (diag.Severity == DiagnosticSeverity.Hidden) continue;

                ScriptDiagnosticSeverity sev = diag.Severity switch
                {
                    DiagnosticSeverity.Error => ScriptDiagnosticSeverity.Error,
                    DiagnosticSeverity.Warning => ScriptDiagnosticSeverity.Warning,
                    _ => ScriptDiagnosticSeverity.Info,
                };

                if (sev == ScriptDiagnosticSeverity.Error) hadErrors = true;

                FileLinePositionSpan span = diag.Location.GetLineSpan();
                diagnosticsSink.Add(new ScriptDiagnostic(
                    sev,
                    diag.GetMessage(),
                    span.Path,
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1));
            }

            if (hadErrors || !emitResult.Success)
                return new CompilationResult(false, null, null);

            return new CompilationResult(
                true,
                peStream.ToArray(),
                pdbStream.Length > 0 ? pdbStream.ToArray() : null);
        }

		#endregion
		#region Reference collection

		private static readonly Lock referenceCacheLock = new();
        private static List<MetadataReference>? cachedBaseReferences;

        private static List<MetadataReference> GetOrBuildBaseReferences()
        {
            lock (referenceCacheLock)
            {
                if (cachedBaseReferences != null) return cachedBaseReferences;

                List<MetadataReference> refs = [];
                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

                // 1. Shared framework - the canonical set of assemblies that make
                //    up the current runtime.
                if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpaList)
                {
                    foreach (string path in tpaList.Split(Path.PathSeparator))
                    {
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        if (!File.Exists(path)) continue;
                        if (!seen.Add(path)) continue;
                        try { refs.Add(MetadataReference.CreateFromFile(path)); }
                        catch (Exception ex)
                        {
                            Debug.Warning(
                                $"Roslyn compiler: could not reference '{path}': {ex.Message}");
                        }
                    }
                }

                // Every user assembly currently loaded with a real Location.
                // this is what lets scripts use engine types, MathLib types,
                // Avalonia types - anything the editor itself has loaded
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic) continue;

                    string? location;
                    try { location = asm.Location; }
                    catch { continue; }

                    if (string.IsNullOrEmpty(location)) continue;
                    if (!File.Exists(location)) continue;
                    if (!seen.Add(location)) continue;

                    try { refs.Add(MetadataReference.CreateFromFile(location)); }
                    catch { /* some assemblies can't be metadata-referenced */ }
                }

                cachedBaseReferences = refs;
                Debug.Info($"Roslyn compiler: cached {refs.Count} metadata references");
                return cachedBaseReferences;
            }
        }

        /// <summary>
        /// Clears the cached base references. Call after loading new assemblies
        /// that scripts should be able to reference.
        /// </summary>
        public static void InvalidateReferenceCache()
        {
            lock (referenceCacheLock) cachedBaseReferences = null;
        }

		#endregion
	}
}
