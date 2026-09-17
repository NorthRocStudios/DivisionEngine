//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Text.Json.Serialization;

namespace DivisionEngine.Serialization
{
    /// <summary>
    /// Represents serializable data for a component in Division Engine.
    /// </summary>
    public class ComponentData
    {
        /// <summary>
        /// Type name of component.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Name of encapsulating assembly for component type.
        /// </summary>
        public string AssemblyName { get; set; }

        /// <summary>
        /// Serialized list of component properties.
        /// </summary>
        public Dictionary<string, string> Properties { get; set; }

        [JsonConstructor]
        public ComponentData()
        {
            TypeName = string.Empty;
            AssemblyName = string.Empty;
            Properties = [];
        }

        /// <summary>
        /// Generates serialized component data.
        /// </summary>
        /// <param name="component">Component to serialize</param>
        public ComponentData(IComponent component)
        {
            Type t = component.GetType();
            // Full name disambiguates when two types share a simple name across namespaces
            TypeName = t.FullName ?? t.Name;
            AssemblyName = t.Assembly.GetName().Name ?? t.Assembly.FullName!;
            Properties = Serialize.Component(component);
        }
    }
}
