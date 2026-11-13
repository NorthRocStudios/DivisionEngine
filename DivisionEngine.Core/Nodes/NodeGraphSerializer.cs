//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DivisionEngine.Nodes
{
    /// <summary>
    /// Raised when a serialized graph cannot be restored.
    /// </summary>
    public sealed class NodeGraphSerializationException : Exception
    {
        /// <summary>Creates a serialization exception with one or more details.</summary>
        /// <param name="message">Summary message.</param>
        /// <param name="details">Individual problems found while loading.</param>
        public NodeGraphSerializationException(string message, IReadOnlyList<string>? details = null)
            : base(message)
        {
            Details = details ?? [];
        }

        /// <summary>Individual problems found while loading the graph.</summary>
        public IReadOnlyList<string> Details { get; }
    }

    /// <summary>
    /// Saves and restores <see cref="NodeGraph"/> instances as JSON. Node types are
    /// resolved through <see cref="NodeTypeRegistry"/>, so any node class used in a
    /// saved graph must be registered (for example with
    /// <see cref="NodeMenuItemAttribute"/>).
    /// </summary>
    public static class NodeGraphSerializer
    {
        /// <summary>Current serialized format version.</summary>
        public const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            IgnoreReadOnlyProperties = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new Float2JsonConverter(),
                new Float3JsonConverter(),
                new Float4JsonConverter(),
            },
        };

        /// <summary>
        /// Serializes a graph to indented JSON.
        /// </summary>
        /// <param name="graph">Graph to serialize.</param>
        public static string Serialize(NodeGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            GraphDto dto = new()
            {
                Version = CurrentVersion,
                Id = graph.Id,
                Name = graph.Name,
            };

            foreach (Node node in graph.Nodes)
            {
                NodeDto nodeDto = new()
                {
                    Id = node.Id,
                    Type = GetTypeId(node),
                    Name = node.Name,
                    X = node.X,
                    Y = node.Y,
                };

                foreach (NodePort port in node.Inputs)
                {
                    nodeDto.Inputs.Add(new PortValueDto
                    {
                        Id = port.Id,
                        Name = port.Name,
                        Value = SerializeValue(port.DefaultValue, port.ValueType),
                    });
                }

                foreach (NodePropertyDescriptor descriptor in NodeReflection.GetProperties(node))
                {
                    nodeDto.Properties[descriptor.Name] =
                        SerializeValue(descriptor.GetValue(node), descriptor.ValueType);
                }

                dto.Nodes.Add(nodeDto);
            }

            foreach (NodeConnection connection in graph.Connections)
            {
                dto.Connections.Add(new ConnectionDto
                {
                    OutputNode = connection.Output.Node.Id,
                    OutputPort = connection.Output.Name,
                    InputNode = connection.Input.Node.Id,
                    InputPort = connection.Input.Name,
                });
            }

            return JsonSerializer.Serialize(dto, Options);
        }

        /// <summary>
        /// Restores a graph from JSON produced by <see cref="Serialize"/>.
        /// </summary>
        /// <param name="json">Serialized graph.</param>
        /// <exception cref="NodeGraphSerializationException">
        /// Thrown when the payload is malformed or references unknown node types,
        /// ports or incompatible connections.
        /// </exception>
        public static NodeGraph Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new NodeGraphSerializationException("The graph payload is empty.");

            GraphDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<GraphDto>(json, Options);
            }
            catch (JsonException ex)
            {
                throw new NodeGraphSerializationException($"The graph payload is not valid JSON: {ex.Message}");
            }

            if (dto is null)
                throw new NodeGraphSerializationException("The graph payload is empty.");
            if (dto.Version > CurrentVersion)
                throw new NodeGraphSerializationException(
                    $"The graph was saved with format version {dto.Version}, but this build supports version {CurrentVersion}.");

            List<string> errors = [];
            NodeGraph graph = new(string.IsNullOrWhiteSpace(dto.Name) ? "Node Graph" : dto.Name);
            if (!string.IsNullOrWhiteSpace(dto.Id)) graph.Id = dto.Id!;
            Dictionary<string, Node> nodesById = new(StringComparer.Ordinal);

            foreach (NodeDto nodeDto in dto.Nodes)
            {
                if (string.IsNullOrWhiteSpace(nodeDto.Type))
                {
                    errors.Add("A node entry is missing its type id.");
                    continue;
                }

                if (!NodeTypeRegistry.TryCreate(nodeDto.Type, out Node? node, out string? error) || node is null)
                {
                    errors.Add(error ?? $"Could not create node type '{nodeDto.Type}'.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(nodeDto.Id)) node.Id = nodeDto.Id!;
                if (!string.IsNullOrWhiteSpace(nodeDto.Name)) node.Name = nodeDto.Name!;
                node.X = nodeDto.X;
                node.Y = nodeDto.Y;

                foreach (PortValueDto portDto in nodeDto.Inputs)
                {
                    NodePort? port = node.FindPort(portDto.Name, NodePortDirection.Input)
                                     ?? node.FindPortById(portDto.Id ?? string.Empty);
                    if (port is null)
                    {
                        errors.Add($"{node.Name}: unknown input port '{portDto.Name}'.");
                        continue;
                    }

                    if (portDto.Value is JsonElement element)
                    {
                        try
                        {
                            port.DefaultValue = DeserializeValue(element, port.ValueType);
                        }
                        catch (Exception ex) when (ex is JsonException or NotSupportedException)
                        {
                            errors.Add($"{node.Name}.{port.Name}: {ex.Message}");
                        }
                    }
                }

                IEnumerable<NodePropertyDescriptor> descriptors = NodeReflection.GetProperties(node);
                foreach ((string name, JsonElement element) in nodeDto.Properties)
                {
                    NodePropertyDescriptor? descriptor = descriptors.FirstOrDefault(d =>
                        string.Equals(d.Name, name, StringComparison.Ordinal));
                    if (descriptor is null)
                    {
                        errors.Add($"{node.Name}: unknown property '{name}'.");
                        continue;
                    }

                    try
                    {
                        descriptor.SetValue(node, DeserializeValue(element, descriptor.ValueType));
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException)
                    {
                        errors.Add($"{node.Name}.{name}: {ex.Message}");
                    }
                }

                try
                {
                    graph.AddNode(node);
                    nodesById[node.Id] = node;
                }
                catch (Exception ex)
                {
                    errors.Add($"{node.Name}: {ex.Message}");
                }
            }

            foreach (ConnectionDto connectionDto in dto.Connections)
            {
                if (!nodesById.TryGetValue(connectionDto.OutputNode, out Node? outputNode))
                {
                    errors.Add($"Connection references unknown output node '{connectionDto.OutputNode}'.");
                    continue;
                }

                if (!nodesById.TryGetValue(connectionDto.InputNode, out Node? inputNode))
                {
                    errors.Add($"Connection references unknown input node '{connectionDto.InputNode}'.");
                    continue;
                }

                NodePort? output = outputNode.FindPort(connectionDto.OutputPort, NodePortDirection.Output);
                NodePort? input = inputNode.FindPort(connectionDto.InputPort, NodePortDirection.Input);
                if (output is null || input is null)
                {
                    errors.Add($"Connection {connectionDto.OutputNode}.{connectionDto.OutputPort} -> " +
                               $"{connectionDto.InputNode}.{connectionDto.InputPort} references a missing port.");
                    continue;
                }

                if (!graph.TryConnect(output, input, out _, out string? error))
                    errors.Add($"Connection {output.Node.Name}.{output.Name} -> {input.Node.Name}.{input.Name}: {error}");
            }

            if (errors.Count > 0)
                throw new NodeGraphSerializationException("The node graph could not be fully restored.", errors);

            return graph;
        }

        /// <summary>
        /// Creates a deep copy of a graph through serialization.
        /// </summary>
        /// <param name="graph">Graph to copy.</param>
        public static NodeGraph Clone(NodeGraph graph) => Deserialize(Serialize(graph));

        private static string GetTypeId(Node node)
            => NodeTypeRegistry.GetRegistration(node.GetType())?.TypeId ?? node.GetType().FullName ?? node.GetType().Name;

        private static JsonElement SerializeValue(object? value, Type? type)
        {
            if (value is null) return JsonSerializer.SerializeToElement<object?>(null, Options);
            Type target = type ?? value.GetType();
            return JsonSerializer.SerializeToElement(value, target, Options);
        }

        private static object? DeserializeValue(JsonElement element, Type? type)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            Type target = type ?? typeof(object);
            return JsonSerializer.Deserialize(element.GetRawText(), target, Options);
        }

        private sealed class GraphDto
        {
            public int Version { get; set; }
            public string? Id { get; set; }
            public string? Name { get; set; }
            public List<NodeDto> Nodes { get; set; } = [];
            public List<ConnectionDto> Connections { get; set; } = [];
        }

        private sealed class NodeDto
        {
            public string? Id { get; set; }
            public string? Type { get; set; }
            public string? Name { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public List<PortValueDto> Inputs { get; set; } = [];
            public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
        }

        private sealed class PortValueDto
        {
            public string? Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public JsonElement Value { get; set; }
        }

        private sealed class ConnectionDto
        {
            public string OutputNode { get; set; } = string.Empty;
            public string OutputPort { get; set; } = string.Empty;
            public string InputNode { get; set; } = string.Empty;
            public string InputPort { get; set; } = string.Empty;
        }

        private sealed class Float2JsonConverter : JsonConverter<float2>
        {
            public override float2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                float[] values = ReadComponents(ref reader, 2);
                return new float2(values[0], values[1]);
            }

            public override void Write(Utf8JsonWriter writer, float2 value, JsonSerializerOptions options)
                => WriteComponents(writer, [value.x, value.y]);

            private static void WriteComponents(Utf8JsonWriter writer, float[] values)
            {
                writer.WriteStartArray();
                foreach (float value in values) writer.WriteNumberValue(value);
                writer.WriteEndArray();
            }

            private static float[] ReadComponents(ref Utf8JsonReader reader, int count)
            {
                float[] values = new float[count];
                int index = 0;
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray && index < count)
                    {
                        values[index++] = (float)reader.GetDouble();
                        reader.Read();
                    }
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        string name = reader.GetString() ?? string.Empty;
                        reader.Read();
                        double value = reader.GetDouble();
                        int slot = name.ToLowerInvariant() switch
                        {
                            "x" or "r" => 0,
                            "y" or "g" => 1,
                            "z" or "b" => 2,
                            "w" or "a" => 3,
                            _ => -1,
                        };
                        if (slot >= 0 && slot < count) values[slot] = (float)value;
                    }
                }
                return values;
            }
        }

        private sealed class Float3JsonConverter : JsonConverter<float3>
        {
            public override float3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                float[] values = ReadComponents(ref reader, 3);
                return new float3(values[0], values[1], values[2]);
            }

            public override void Write(Utf8JsonWriter writer, float3 value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(value.x);
                writer.WriteNumberValue(value.y);
                writer.WriteNumberValue(value.z);
                writer.WriteEndArray();
            }

            private static float[] ReadComponents(ref Utf8JsonReader reader, int count)
            {
                float[] values = new float[count];
                int index = 0;
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray && index < count)
                    {
                        values[index++] = (float)reader.GetDouble();
                        reader.Read();
                    }
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        string name = reader.GetString() ?? string.Empty;
                        reader.Read();
                        double value = reader.GetDouble();
                        int slot = name.ToLowerInvariant() switch
                        {
                            "x" or "r" => 0,
                            "y" or "g" => 1,
                            "z" or "b" => 2,
                            _ => -1,
                        };
                        if (slot >= 0 && slot < count) values[slot] = (float)value;
                    }
                }
                return values;
            }
        }

        private sealed class Float4JsonConverter : JsonConverter<float4>
        {
            public override float4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                float[] values = new float[4];
                int index = 0;
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray && index < 4)
                    {
                        values[index++] = (float)reader.GetDouble();
                        reader.Read();
                    }
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        string name = reader.GetString() ?? string.Empty;
                        reader.Read();
                        double value = reader.GetDouble();
                        int slot = name.ToLowerInvariant() switch
                        {
                            "x" or "r" => 0,
                            "y" or "g" => 1,
                            "z" or "b" => 2,
                            "w" or "a" => 3,
                            _ => -1,
                        };
                        if (slot >= 0) values[slot] = (float)value;
                    }
                }
                return new float4(values[0], values[1], values[2], values[3]);
            }

            public override void Write(Utf8JsonWriter writer, float4 value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(value.x);
                writer.WriteNumberValue(value.y);
                writer.WriteNumberValue(value.z);
                writer.WriteNumberValue(value.w);
                writer.WriteEndArray();
            }
        }
    }
}
