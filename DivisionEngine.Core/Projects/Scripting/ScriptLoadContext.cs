//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Reflection;
using System.Runtime.Loader;

namespace DivisionEngine.Projects.Scripting
{
    /// <summary>
    /// Collectible <see cref="AssemblyLoadContext"/> that owns the compiled
    /// player and editor script assemblies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The critical rule: framework and engine assemblies must resolve to the
    /// <b>default</b> load context, not to ours. If we returned a private copy
    /// of <c>DivisionEngine.dll</c>, then <c>IComponent</c> from a script would
    /// be a different runtime type than <c>IComponent</c> in the host - every
    /// <c>typeof(IComponent).IsAssignableFrom(t)</c> check would return false
    /// and nothing would work.
    /// </para>
    /// <para>
    /// Our <see cref="Load"/> override therefore returns <c>null</c> for any
    /// assembly that isn't one of the script assemblies we explicitly own,
    /// which delegates resolution to the default context.
    /// </para>
    /// </remarks>
    public sealed class ScriptLoadContext : AssemblyLoadContext
    {
        // Compiled assemblies we own, keyed by simple name
        private readonly Dictionary<string, CompiledAssembly> owned = [];

        // Loaded Assembly instances, keyed by simple name
        private readonly Dictionary<string, Assembly> loaded = [];

        public ScriptLoadContext() : base("DivisionScripts", isCollectible: true) { }

        /// <summary>
        /// All assemblies loaded through this context.
        /// </summary>
        public IReadOnlyCollection<Assembly> LoadedAssemblies => loaded.Values;

        /// <summary>
        /// Gets a loaded assembly by simple name, or null if not loaded here.
        /// </summary>
        public Assembly? GetAssembly(string name) =>
            loaded.TryGetValue(name, out Assembly? asm) ? asm : null;

        /// <summary>
        /// Loads a compiled assembly into this context.
        /// </summary>
        public Assembly LoadCompiled(CompiledAssembly assembly)
        {
            owned[assembly.Name] = assembly;

            using MemoryStream imageStream = new(assembly.Image);
            using MemoryStream? pdbStream = assembly.Pdb != null
                ? new MemoryStream(assembly.Pdb)
                : null;

            Assembly loadedAsm = pdbStream != null
                ? LoadFromStream(imageStream, pdbStream)
                : LoadFromStream(imageStream);

            loaded[assembly.Name] = loadedAsm;
            Debug.Info($"Script Load Context: loaded '{assembly.Name}' ({assembly.Image.Length:N0} bytes)");
            return loadedAsm;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null) return null;

            // Already loaded? return the cached instance no need to reload
            if (loaded.TryGetValue(assemblyName.Name, out Assembly? existing)) return existing;

            // Own it but haven't loaded it yet (e.g. the editor referencing the
            // player, where player was compiled but not explicitly loaded)
            if (owned.TryGetValue(assemblyName.Name, out CompiledAssembly? assembly))
            {
                using MemoryStream imageStream = new(assembly.Image);
                using MemoryStream? pdbStream = assembly.Pdb != null
                    ? new MemoryStream(assembly.Pdb)
                    : null;

                Assembly loadedAsm = pdbStream != null
                    ? LoadFromStream(imageStream, pdbStream)
                    : LoadFromStream(imageStream);

                loaded[assemblyName.Name] = loadedAsm;
                return loadedAsm;
            }

            // Not ours let the default context resolve it
            return null;
        }
    }
}
