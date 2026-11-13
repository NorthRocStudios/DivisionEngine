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
    /// Inputs resolved by the graph for one node evaluation. Values come either from
    /// an upstream connection or from the input port's default, so nodes never have to
    /// special-case whether a port is wired.
    /// </summary>
    public sealed class NodeEvaluationContext
    {
        private readonly Dictionary<string, IReadOnlyList<object?>> values;
        private readonly HashSet<string> connected;

        internal NodeEvaluationContext(
            Node node,
            NodeGraph graph,
            Dictionary<string, IReadOnlyList<object?>> values,
            HashSet<string> connected)
        {
            Node = node;
            Graph = graph;
            this.values = values;
            this.connected = connected;
        }

        /// <summary>The node being evaluated.</summary>
        public Node Node { get; }

        /// <summary>The owning graph.</summary>
        public NodeGraph Graph { get; }

        /// <summary>
        /// Returns true when the named input is supplied by at least one connection.
        /// </summary>
        /// <param name="portName">Input port name.</param>
        public bool IsConnected(string portName) => connected.Contains(portName);

        /// <summary>
        /// Returns every value resolved for an input port. Single-connection ports
        /// always contain exactly one value (the connection value or the default).
        /// </summary>
        /// <param name="portName">Input port name.</param>
        public IReadOnlyList<object?> GetInputs(string portName)
        {
            if (values.TryGetValue(portName, out IReadOnlyList<object?>? list)) return list;
            return [];
        }

        /// <summary>
        /// Returns the first resolved value of an input port, or null.
        /// </summary>
        /// <param name="portName">Input port name.</param>
        public object? GetInput(string portName)
        {
            IReadOnlyList<object?> list = GetInputs(portName);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>
        /// Returns a typed input value, converting numeric primitives when possible.
        /// </summary>
        /// <typeparam name="T">Requested value type.</typeparam>
        /// <param name="portName">Input port name.</param>
        /// <returns>The converted value, or the default of <typeparamref name="T"/>.</returns>
        public T GetInput<T>(string portName)
        {
            object? value = GetInput(portName);
            if (value is T typed) return typed;

            if (value is IConvertible)
            {
                try
                {
                    if (typeof(T) == typeof(float)) return (T)(object)Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(bool)) return (T)(object)Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (FormatException) { }
                catch (InvalidCastException) { }
                catch (OverflowException) { }
            }

            return default!;
        }

        /// <summary>
        /// Returns a typed input value, or <paramref name="fallback"/> when the input
        /// is missing or not convertible.
        /// </summary>
        /// <typeparam name="T">Requested value type.</typeparam>
        /// <param name="portName">Input port name.</param>
        /// <param name="fallback">Value to return when conversion fails.</param>
        public T GetInput<T>(string portName, T fallback)
        {
            object? value = GetInput(portName);
            if (value is T typed) return typed;

            if (value is IConvertible)
            {
                try
                {
                    if (typeof(T) == typeof(float)) return (T)(object)Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(bool)) return (T)(object)Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (FormatException) { }
                catch (InvalidCastException) { }
                catch (OverflowException) { }
            }

            return fallback;
        }
    }
}
