using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Storage.Internal;

/// <summary>Reads and writes a single <see cref="CodeNode"/> to/from the binary format.</summary>
internal static class NodeBinarySerializer
{
    public static void Write(BinaryWriter writer, CodeNode node)
    {
        writer.Write(node.Id);
        writer.Write(node.Name);
        WriteStringList(writer, node.ScopeChain);
        writer.Write(node.ScopeSeparator);
        writer.Write(node.QualifiedName);
        writer.Write((int)node.Kind);

        writer.Write(node.Location.FilePath);
        writer.Write(node.Location.StartLine);
        writer.Write(node.Location.EndLine);

        WriteSummary(writer, node.Summary);

        writer.Write(node.Body);
        WriteMetadata(writer, node.Metadata);
        writer.Write(node.ContentHash);

        writer.Write(node.Edges.Count);
        foreach (var edge in node.Edges)
        {
            writer.Write((int)edge.Kind);
            writer.Write(edge.TargetNodeId);
        }

        WriteStringList(writer, node.SkippedRelationships);
    }

    public static CodeNode Read(BinaryReader reader)
    {
        var id = reader.ReadString();
        var name = reader.ReadString();
        var scopeChain = ReadStringList(reader);
        var scopeSeparator = reader.ReadString();
        var qualifiedName = reader.ReadString();
        var kind = (NodeKind)reader.ReadInt32();

        var filePath = reader.ReadString();
        var startLine = reader.ReadInt32();
        var endLine = reader.ReadInt32();

        var summary = ReadSummary(reader);

        var body = reader.ReadString();
        var metadata = ReadMetadata(reader);
        var contentHash = reader.ReadString();

        var edgeCount = reader.ReadInt32();
        var edges = new NodeEdge[edgeCount];
        for (var i = 0; i < edgeCount; i++)
        {
            var edgeKind = (EdgeKind)reader.ReadInt32();
            var targetId = reader.ReadString();
            edges[i] = new NodeEdge { Kind = edgeKind, TargetNodeId = targetId };
        }

        var skippedRelationships = ReadStringList(reader);

        return new CodeNode
        {
            Id = id,
            Name = name,
            ScopeChain = scopeChain,
            ScopeSeparator = scopeSeparator,
            QualifiedName = qualifiedName,
            Kind = kind,
            Location = new NodeLocation { FilePath = filePath, StartLine = startLine, EndLine = endLine },
            Summary = summary,
            Body = body,
            Metadata = metadata,
            ContentHash = contentHash,
            Edges = edges,
            SkippedRelationships = skippedRelationships,
        };
    }

    private static void WriteSummary(BinaryWriter writer, NodeSummary summary)
    {
        writer.Write(summary.Name);
        writer.Write(summary.Signature);

        writer.Write(summary.Parameters.Count);
        foreach (var parameter in summary.Parameters)
        {
            writer.Write(parameter.Name);
            writer.Write(parameter.Type);
        }

        WriteNullableString(writer, summary.ReturnType);
        WriteNullableString(writer, summary.DocComment);
        writer.Write(summary.LineCount);
    }

    private static NodeSummary ReadSummary(BinaryReader reader)
    {
        var name = reader.ReadString();
        var signature = reader.ReadString();

        var paramCount = reader.ReadInt32();
        var parameters = new ParameterInfo[paramCount];
        for (var i = 0; i < paramCount; i++)
        {
            var pName = reader.ReadString();
            var pType = reader.ReadString();
            parameters[i] = new ParameterInfo { Name = pName, Type = pType };
        }

        var returnType = ReadNullableString(reader);
        var docComment = ReadNullableString(reader);
        var lineCount = reader.ReadInt32();

        return new NodeSummary
        {
            Name = name,
            Signature = signature,
            Parameters = parameters,
            ReturnType = returnType,
            DocComment = docComment,
            LineCount = lineCount,
        };
    }

    private static void WriteMetadata(BinaryWriter writer, NodeMetadata metadata)
    {
        writer.Write(metadata.IsPublic);
        writer.Write(metadata.IsPrivate);
        writer.Write(metadata.IsProtected);
        writer.Write(metadata.IsInternal);
        writer.Write(metadata.IsStatic);
        writer.Write(metadata.IsAsync);
        writer.Write(metadata.IsAbstract);
        writer.Write(metadata.IsVirtual);
        writer.Write(metadata.IsOverride);
        writer.Write(metadata.IsTest);

        writer.Write(metadata.Extra.Count);
        foreach (var (key, value) in metadata.Extra)
        {
            writer.Write(key);
            writer.Write(value);
        }
    }

    private static NodeMetadata ReadMetadata(BinaryReader reader)
    {
        var isPublic = reader.ReadBoolean();
        var isPrivate = reader.ReadBoolean();
        var isProtected = reader.ReadBoolean();
        var isInternal = reader.ReadBoolean();
        var isStatic = reader.ReadBoolean();
        var isAsync = reader.ReadBoolean();
        var isAbstract = reader.ReadBoolean();
        var isVirtual = reader.ReadBoolean();
        var isOverride = reader.ReadBoolean();
        var isTest = reader.ReadBoolean();

        var extraCount = reader.ReadInt32();
        var extra = new Dictionary<string, string>(extraCount);
        for (var i = 0; i < extraCount; i++)
        {
            var key = reader.ReadString();
            var value = reader.ReadString();
            extra[key] = value;
        }

        return new NodeMetadata
        {
            IsPublic = isPublic,
            IsPrivate = isPrivate,
            IsProtected = isProtected,
            IsInternal = isInternal,
            IsStatic = isStatic,
            IsAsync = isAsync,
            IsAbstract = isAbstract,
            IsVirtual = isVirtual,
            IsOverride = isOverride,
            IsTest = isTest,
            Extra = extra,
        };
    }

    private static void WriteStringList(BinaryWriter writer, IReadOnlyList<string> values)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static string[] ReadStringList(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new string[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = reader.ReadString();
        }

        return values;
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static string? ReadNullableString(BinaryReader reader)
    {
        var hasValue = reader.ReadBoolean();
        return hasValue ? reader.ReadString() : null;
    }
}
