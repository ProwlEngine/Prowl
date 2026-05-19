namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Object/connection graph extracted from a parsed FBX node tree.
/// </summary>
/// <remarks>
/// FBX layout: every persisted object lives under <c>Objects/</c> with a unique 64-bit ID, a
/// "class" tag (Mesh, Material, Texture, etc.) and a name. Relationships go through
/// <c>Connections/</c> records: each is either OO (object-to-object) or OP (object-to-property),
/// pairing a source ID with a destination ID (plus an optional property name for OP).
/// We index those connections both by source and destination for fast lookups during mapping.
/// </remarks>
internal sealed class FbxDocument
{
    public uint Version { get; init; }
    public string? Creator { get; init; }
    public FbxNode Root { get; }
    public FbxPropertyTable GlobalSettings { get; }
    public Dictionary<long, FbxObject> Objects { get; } = new();
    public List<FbxConnection> Connections { get; } = new();

    /// <summary>Connections keyed by their <see cref="FbxConnection.Source"/> id.</summary>
    public Dictionary<long, List<FbxConnection>> ConnectionsBySource { get; } = new();

    /// <summary>Connections keyed by their <see cref="FbxConnection.Destination"/> id.</summary>
    public Dictionary<long, List<FbxConnection>> ConnectionsByDestination { get; } = new();

    /// <summary>Property templates declared under <c>Definitions/</c>, keyed by class tag.</summary>
    public Dictionary<string, FbxPropertyTable> Templates { get; } = new();

    public FbxDocument(FbxNode root, uint version, string? creator)
    {
        Root = root;
        Version = version;
        Creator = creator;

        // Read Definitions/ -> property templates (default values for the various object types).
        ReadTemplates();

        // Read GlobalSettings.
        var gs = root.FindChild("GlobalSettings");
        var gsProps = gs?.FindChild("Properties70") ?? gs?.FindChild("Properties60");
        GlobalSettings = gsProps is null
            ? new FbxPropertyTable()
            : FbxPropertyTable.From(gsProps);

        // Read every object under Objects/.
        var objectsNode = root.FindChild("Objects");
        if (objectsNode is not null)
        {
            foreach (var child in objectsNode.Children)
                IngestObject(child);
        }

        // Read connections.
        var connNode = root.FindChild("Connections");
        if (connNode is not null)
        {
            foreach (var c in connNode.Children.Where(c => c.Name == "C" || c.Name == "Connect"))
                IngestConnection(c);
        }
    }

    private void ReadTemplates()
    {
        var defs = Root.FindChild("Definitions");
        if (defs is null) return;
        foreach (var objType in defs.FindChildren("ObjectType"))
        {
            string className = objType.StringAt(0);
            var template = objType.FindChild("PropertyTemplate");
            var props = template?.FindChild("Properties70") ?? template?.FindChild("Properties60");
            if (props is not null)
                Templates[className] = FbxPropertyTable.From(props);
        }
    }

    private void IngestObject(FbxNode node)
    {
        // Properties[0] is the object's u64 ID. Properties[1] is the name (may contain a binary
        // \0\1 separator separating "ClassName\0\1Namespace"; convert to "Namespace::ClassName"
        // because most of the FBX SDK speaks the namespace-prefixed form).
        // Properties[2] is the class subtype (e.g. "Mesh" for Geometry::Mesh).
        // Some exporters drop non-object metadata records into Objects/; skip them silently if
        // the first property isn't a numeric ID.
        if (node.Properties.Count < 3) return;
        var p0 = node.Properties[0];
        if (p0.Type != FbxPropertyType.Int16 &&
            p0.Type != FbxPropertyType.Int32 &&
            p0.Type != FbxPropertyType.Int64)
            return;
        long id = p0.AsLong();
        string rawName = node.Properties[1].AsString();
        string subtype = node.Properties[2].AsString();

        var (cleanName, _namespace) = SplitBinaryName(rawName);

        var props = node.FindChild("Properties70") ?? node.FindChild("Properties60");
        var propTable = props is not null ? FbxPropertyTable.From(props) : new FbxPropertyTable();

        var obj = new FbxObject
        {
            Id = id,
            ObjectType = node.Name,
            Subtype = subtype,
            Name = cleanName,
            Node = node,
            Properties = propTable,
        };
        Objects[id] = obj;
    }

    private void IngestConnection(FbxNode c)
    {
        // Connection layouts in the wild:
        //   OO : "OO", srcId(L), dstId(L)
        //   OP : "OP", srcId(L), dstId(L), property(S)
        //   PO : "PO", srcId(L), property(S), dstId(L)
        //   PP : "PP", srcId(L), srcProp(S), dstId(L), dstProp(S)
        // We skip PP entirely (property-to-property; not used by us). For everything else we
        // scan the trailing properties for the two long IDs (in encounter order = src, dst) and
        // the optional property-name string. This is more robust than fixed indexing because
        // some exporters emit slightly non-standard layouts.
        if (c.Properties.Count < 3) return;
        string type = c.Properties[0].AsString();
        if (type == "PP") return;

        long src = 0, dst = 0;
        string property = string.Empty;
        int longSlot = 0;
        for (int i = 1; i < c.Properties.Count; i++)
        {
            var p = c.Properties[i];
            switch (p.Type)
            {
                case FbxPropertyType.Int16:
                case FbxPropertyType.Int32:
                case FbxPropertyType.Int64:
                    if (longSlot == 0) src = p.AsLong();
                    else if (longSlot == 1) dst = p.AsLong();
                    longSlot++;
                    break;
                case FbxPropertyType.String:
                case FbxPropertyType.Raw:
                    if (property.Length == 0) property = p.AsString();
                    break;
            }
        }

        if (longSlot < 2)
            return; // No usable src/dst pair found.

        var conn = new FbxConnection
        {
            Type = type,
            Source = src,
            Destination = dst,
            Property = property,
        };
        Connections.Add(conn);
        AddIndex(ConnectionsBySource, src, conn);
        AddIndex(ConnectionsByDestination, dst, conn);
    }

    private static void AddIndex(Dictionary<long, List<FbxConnection>> idx, long key, FbxConnection conn)
    {
        if (!idx.TryGetValue(key, out var list))
        {
            list = new List<FbxConnection>(2);
            idx[key] = list;
        }
        list.Add(conn);
    }

    /// <summary>
    /// Binary FBX encodes namespaced object names as "ClassName\0\1Namespace". The ASCII
    /// equivalent is "Namespace::ClassName". Returns the parsed namespace and the clean name.
    /// </summary>
    private static (string Name, string Namespace) SplitBinaryName(string raw)
    {
        int sep = raw.IndexOf('\0');
        if (sep >= 0 && sep + 1 < raw.Length && raw[sep + 1] == '')
        {
            string className = raw[..sep];
            string ns = raw[(sep + 2)..];
            return (className, ns);
        }
        // ASCII form already namespaced.
        int dc = raw.IndexOf("::", StringComparison.Ordinal);
        if (dc >= 0)
            return (raw[(dc + 2)..], raw[..dc]);
        return (raw, string.Empty);
    }

    /// <summary>Returns every object connected as a source to <paramref name="destinationId"/>.</summary>
    public IEnumerable<FbxObject> GetSourceObjects(long destinationId, string? typeFilter = null)
    {
        if (!ConnectionsByDestination.TryGetValue(destinationId, out var list))
            yield break;
        foreach (var c in list)
        {
            if (!Objects.TryGetValue(c.Source, out var obj)) continue;
            if (typeFilter is not null && obj.ObjectType != typeFilter) continue;
            yield return obj;
        }
    }

    /// <summary>Returns every object connected as a destination from <paramref name="sourceId"/>.</summary>
    public IEnumerable<FbxObject> GetDestinationObjects(long sourceId, string? typeFilter = null)
    {
        if (!ConnectionsBySource.TryGetValue(sourceId, out var list))
            yield break;
        foreach (var c in list)
        {
            if (!Objects.TryGetValue(c.Destination, out var obj)) continue;
            if (typeFilter is not null && obj.ObjectType != typeFilter) continue;
            yield return obj;
        }
    }
}

internal sealed class FbxObject
{
    public required long Id { get; init; }
    /// <summary>Outer container name, e.g. "Model", "Geometry", "Material", "Texture", "Video".</summary>
    public required string ObjectType { get; init; }
    /// <summary>Subtype string at property index 2, e.g. "Mesh", "LimbNode", "Light".</summary>
    public required string Subtype { get; init; }
    /// <summary>Cleaned object name (binary 0/1 separator removed).</summary>
    public required string Name { get; init; }
    /// <summary>The raw node, so mappers can pull custom children.</summary>
    public required FbxNode Node { get; init; }
    /// <summary>Parsed property70/60 table; empty when the object has none.</summary>
    public required FbxPropertyTable Properties { get; init; }
}

internal sealed class FbxConnection
{
    public required string Type { get; init; }
    public required long Source { get; init; }
    public required long Destination { get; init; }
    public required string Property { get; init; }
}
