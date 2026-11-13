//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Reflection;

namespace DivisionEngine.Nodes
{
    /// <summary>
    /// Metadata for a node type that can be created from the add-node menu.
    /// </summary>
    public sealed class NodeTypeRegistration
    {
        internal NodeTypeRegistration(Type nodeType, string menuPath, string? description, string? icon)
        {
            NodeType = nodeType;
            TypeId = nodeType.FullName ?? nodeType.Name;
            MenuPath = menuPath;
            Description = description;
            Icon = icon;

            string[] segments = menuPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            DisplayName = segments.Length > 0 ? segments[^1] : nodeType.Name;
            Category = segments.Length > 1 ? segments[0] : "General";
        }

        /// <summary>CLR type of the node.</summary>
        public Type NodeType { get; }

        /// <summary>Stable type id used by serialization (the full type name).</summary>
        public string TypeId { get; }

        /// <summary>Full slash separated menu path.</summary>
        public string MenuPath { get; }

        /// <summary>Display name (last menu segment).</summary>
        public string DisplayName { get; }

        /// <summary>Top level menu category.</summary>
        public string Category { get; }

        /// <summary>Optional description.</summary>
        public string? Description { get; }

        /// <summary>Optional icon name resolved by the editor.</summary>
        public string? Icon { get; }

        /// <summary>
        /// Creates a new node instance for this registration.
        /// </summary>
        /// <returns>A new node.</returns>
        public Node CreateInstance()
            => (Node)(Activator.CreateInstance(NodeType)
                ?? throw new InvalidOperationException($"Could not create node type {NodeType.FullName}."));
    }

    /// <summary>
    /// Central registry of creatable node types, populated by scanning assemblies for
    /// <see cref="NodeMenuItemAttribute"/>. Editors can register additional node types
    /// at runtime with <see cref="Register{T}"/>.
    /// </summary>
    public static class NodeTypeRegistry
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, NodeTypeRegistration> ById = new(StringComparer.Ordinal);
        private static readonly HashSet<Assembly> ScannedAssemblies = [];

        static NodeTypeRegistry()
        {
            EnsureAssembly(typeof(Node).Assembly);
        }

        /// <summary>All registered node types, ordered by menu path.</summary>
        public static IReadOnlyList<NodeTypeRegistration> All
        {
            get
            {
                lock (Gate)
                {
                    return ById.Values
                        .OrderBy(r => r.MenuPath, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }
        }

        /// <summary>
        /// Scans an assembly for node types exactly once.
        /// </summary>
        /// <param name="assembly">Assembly to scan.</param>
        public static void EnsureAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            lock (Gate)
            {
                if (!ScannedAssemblies.Add(assembly)) return;
            }

            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null)!;
            }

            foreach (Type type in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (type is null || type.IsAbstract || type.IsInterface || !typeof(Node).IsAssignableFrom(type)) continue;
                NodeMenuItemAttribute? attribute = type.GetCustomAttribute<NodeMenuItemAttribute>(inherit: false);
                if (attribute is null) continue;
                Register(type, attribute.MenuPath, attribute.Description, attribute.Icon);
            }
        }

        /// <summary>
        /// Registers a node type with a menu path.
        /// </summary>
        /// <param name="type">Concrete node type with a parameterless constructor.</param>
        /// <param name="menuPath">Slash separated menu path.</param>
        /// <param name="description">Optional description.</param>
        /// <param name="icon">Optional icon name.</param>
        public static void Register(Type type, string menuPath, string? description = null, string? icon = null)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (!typeof(Node).IsAssignableFrom(type) || type.IsAbstract)
                throw new ArgumentException($"{type.FullName} is not a concrete Node type.", nameof(type));
            if (type.GetConstructor(Type.EmptyTypes) is null)
                throw new ArgumentException($"{type.FullName} needs a public parameterless constructor.", nameof(type));
            if (string.IsNullOrWhiteSpace(menuPath))
                throw new ArgumentException("A node menu path is required.", nameof(menuPath));

            string normalized = string.Join('/', menuPath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            lock (Gate)
            {
                string typeId = type.FullName ?? type.Name;
                if (ById.TryGetValue(typeId, out NodeTypeRegistration? existing))
                {
                    if (existing.NodeType == type) return;
                    throw new InvalidOperationException($"Two node types share the id '{typeId}'.");
                }

                ById[typeId] = new NodeTypeRegistration(type, normalized, description, icon);
            }
        }

        /// <summary>
        /// Registers a node type with a menu path.
        /// </summary>
        /// <typeparam name="T">Concrete node type with a parameterless constructor.</typeparam>
        /// <param name="menuPath">Slash separated menu path.</param>
        /// <param name="description">Optional description.</param>
        /// <param name="icon">Optional icon name.</param>
        public static void Register<T>(string menuPath, string? description = null, string? icon = null)
            where T : Node, new()
            => Register(typeof(T), menuPath, description, icon);

        /// <summary>
        /// Looks up the registration for a node instance or type.
        /// </summary>
        /// <param name="type">Node type to look for.</param>
        /// <returns>Registration, or null for unregistered custom types.</returns>
        public static NodeTypeRegistration? GetRegistration(Type type)
        {
            lock (Gate)
            {
                return ById.TryGetValue(type.FullName ?? type.Name, out NodeTypeRegistration? registration)
                    ? registration
                    : null;
            }
        }

        /// <summary>
        /// Creates a node by stable type id.
        /// </summary>
        /// <param name="typeId">Full type name stored in serialized graphs.</param>
        /// <param name="node">Created node on success.</param>
        /// <param name="error">Failure reason.</param>
        /// <returns>True when a node was created.</returns>
        public static bool TryCreate(string typeId, out Node? node, out string? error)
        {
            node = null;
            error = null;
            NodeTypeRegistration? registration;
            lock (Gate) ById.TryGetValue(typeId, out registration);
            if (registration is null)
            {
                error = $"Unknown node type '{typeId}'. The type is not registered in NodeTypeRegistry.";
                return false;
            }

            try
            {
                node = registration.CreateInstance();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not create node '{typeId}': {ex.Message}";
                return false;
            }
        }
    }
}
