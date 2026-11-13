//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Nodes
{
    /// <summary>
    /// Marks a <see cref="Node"/> class as creatable from the node editor's add-node
    /// menu. The menu path uses forward slashes to nest categories, for example
    /// <c>"SDF/Primitives/Sphere"</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NodeMenuItemAttribute : Attribute
    {
        /// <summary>
        /// Creates a menu registration attribute.
        /// </summary>
        /// <param name="menuPath">Slash separated menu path.</param>
        public NodeMenuItemAttribute(string menuPath)
        {
            MenuPath = menuPath;
        }

        /// <summary>Slash separated menu path.</summary>
        public string MenuPath { get; }

        /// <summary>Optional description shown in the add-node list and inspector.</summary>
        public string? Description { get; set; }

        /// <summary>Optional material icon name used by the editor.</summary>
        public string? Icon { get; set; }
    }

    /// <summary>
    /// Marks a public field or property on a <see cref="Node"/> as a quickly editable
    /// parameter. Parameters appear in the node inspector and are serialized with the
    /// graph without any extra code.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class NodePropertyAttribute : Attribute
    {
        /// <summary>Optional label; defaults to a spaced version of the member name.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Inclusive lower bound for numeric values.</summary>
        public double Min { get; set; } = double.NaN;

        /// <summary>Inclusive upper bound for numeric values.</summary>
        public double Max { get; set; } = double.NaN;

        /// <summary>Editor increment for numeric values. NaN infers one from the type.</summary>
        public double Step { get; set; } = double.NaN;

        /// <summary>Optional editor tooltip.</summary>
        public string? Tooltip { get; set; }

        /// <summary>When true the property is serialized but hidden from the inspector.</summary>
        public bool HideInInspector { get; set; }
    }
}
