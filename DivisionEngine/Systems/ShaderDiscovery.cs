//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Graphics;
using DivisionEngine.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Finds every <see cref="ShaderAttribute"/> struct in the loaded engine assemblies.
    /// </summary>
    internal static class ShaderDiscovery
    {
        /// <summary>
        /// Every non-generic shader struct known to the process, ordered by name.
        /// </summary>
        public static List<Type> Discover()
        {
            // A shader struct lives in Core (or in a script assembly); make sure the
            // engine assemblies are loaded before scanning.
            _ = typeof(RenderPipeline).Assembly;
            _ = typeof(ShaderDiscovery).Assembly;

            var types = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name ?? string.Empty;
                if (!name.StartsWith("DivisionEngine", StringComparison.Ordinal)) continue;

                Type[] candidates;
                try
                {
                    candidates = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    candidates = [.. ex.Types.Where(t => t is not null).Cast<Type>()];
                }

                foreach (Type type in candidates)
                {
                    if (!type.IsValueType || type.IsGenericTypeDefinition) continue;
                    if (type.GetCustomAttribute<ShaderAttribute>() is null) continue;
                    if (type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null) continue;
                    types.Add(type);
                }
            }

            return [.. types.OrderBy(t => t.Name, StringComparer.Ordinal)];
        }
    }
}
