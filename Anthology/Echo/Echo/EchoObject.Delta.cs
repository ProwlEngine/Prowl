// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public sealed partial class EchoObject
{
    /// <summary>
    /// Creates a delta representing the differences between two EchoObjects.
    /// The delta is itself an EchoObject that can be serialized.
    /// </summary>
    /// <param name="from">The baseline/original EchoObject</param>
    /// <param name="to">The target/modified EchoObject</param>
    /// <returns>An EchoObject containing the delta operations</returns>
    public static EchoObject CreateDelta(EchoObject from, EchoObject to)
    {
        EchoObject delta = NewCompound();
        EchoObject operations = NewList();
        delta.Add("Operations", operations);

        CompareRecursive(from, to, "", operations);

        return delta;
    }

    /// <summary>
    /// Applies a delta to a baseline EchoObject, producing a new modified EchoObject.
    /// </summary>
    /// <param name="baseline">The baseline EchoObject to apply the delta to</param>
    /// <param name="delta">The delta EchoObject created by CreateDelta</param>
    /// <returns>A new EchoObject with the delta applied</returns>
    public static EchoObject ApplyDelta(EchoObject baseline, EchoObject delta)
    {
        EchoObject result = baseline.Clone();

        if (!delta.TryGet("Operations", out EchoObject? operationsList))
            throw new ArgumentException("Delta does not contain Operations list", nameof(delta));

        foreach (EchoObject operation in operationsList!.List)
        {
            ApplyOperation(result, operation);
        }

        return result;
    }

    private static void CompareRecursive(EchoObject from, EchoObject to, string path, EchoObject operations)
    {
        // If the types are different, replace entirely
        if (from.TagType != to.TagType)
        {
            AddSetValueOperation(operations, path, to);
            return;
        }

        switch (to.TagType)
        {
            case EchoType.Compound:
                CompareCompound(from, to, path, operations);
                break;
            case EchoType.List:
                CompareList(from, to, path, operations);
                break;
            default:
                // Primitive comparison
                if (!from.Equals(to))
                {
                    AddSetValueOperation(operations, path, to);
                }
                break;
        }
    }

    private static void CompareCompound(EchoObject from, EchoObject to, string path, EchoObject operations)
    {
        CompoundTags fromTags = from.Tags;
        CompoundTags toTags = to.Tags;

        // Find removed keys
        foreach (string key in fromTags.Keys)
        {
            if (!toTags.ContainsKey(key))
            {
                AddRemoveCompoundTagOperation(operations, path, key);
            }
        }

        // Find added or changed keys
        foreach ((string? key, EchoObject? toValue) in toTags)
        {
            string escapedKey = EscapePathSegment(key);
            string childPath = string.IsNullOrEmpty(path) ? escapedKey : $"{path}/{escapedKey}";

            if (!fromTags.ContainsKey(key))
            {
                // Key was added
                AddAddCompoundTagOperation(operations, path, key, toValue);
            }
            else
            {
                // Key exists in both - compare recursively
                CompareRecursive(fromTags[key], toValue, childPath, operations);
            }
        }
    }

    private static void CompareList(EchoObject from, EchoObject to, string path, EchoObject operations)
    {
        List<EchoObject> fromList = from.List;
        List<EchoObject> toList = to.List;

        int minLen = Math.Min(fromList.Count, toList.Count);

        // Compare existing elements
        for (int i = 0; i < minLen; i++)
        {
            string childPath = string.IsNullOrEmpty(path) ? i.ToString() : $"{path}/{i}";
            CompareRecursive(fromList[i], toList[i], childPath, operations);
        }

        // Handle added elements
        if (toList.Count > fromList.Count)
        {
            for (int i = fromList.Count; i < toList.Count; i++)
            {
                AddAddListItemOperation(operations, path, i, toList[i]);
            }
        }
        // Handle removed elements (remove from end backwards)
        else if (toList.Count < fromList.Count)
        {
            for (int i = fromList.Count - 1; i >= toList.Count; i--)
            {
                AddRemoveListItemOperation(operations, path, i);
            }
        }
    }

    private static void AddSetValueOperation(EchoObject operations, string path, EchoObject value)
    {
        EchoObject op = NewCompound();
        op.Add("Type", new EchoObject("SetValue"));
        op.Add("Path", new EchoObject(path));
        op.Add("Value", value.Clone());
        operations.ListAdd(op);
    }

    private static void AddAddCompoundTagOperation(EchoObject operations, string path, string key, EchoObject value)
    {
        EchoObject op = NewCompound();
        op.Add("Type", new EchoObject("AddCompoundTag"));
        op.Add("Path", new EchoObject(path));
        op.Add("Key", new EchoObject(key));
        op.Add("Value", value.Clone());
        operations.ListAdd(op);
    }

    private static void AddRemoveCompoundTagOperation(EchoObject operations, string path, string key)
    {
        EchoObject op = NewCompound();
        op.Add("Type", new EchoObject("RemoveCompoundTag"));
        op.Add("Path", new EchoObject(path));
        op.Add("Key", new EchoObject(key));
        operations.ListAdd(op);
    }

    private static void AddAddListItemOperation(EchoObject operations, string path, int index, EchoObject value)
    {
        EchoObject op = NewCompound();
        op.Add("Type", new EchoObject("AddListItem"));
        op.Add("Path", new EchoObject(path));
        op.Add("Index", new EchoObject(index));
        op.Add("Value", value.Clone());
        operations.ListAdd(op);
    }

    private static void AddRemoveListItemOperation(EchoObject operations, string path, int index)
    {
        EchoObject op = NewCompound();
        op.Add("Type", new EchoObject("RemoveListItem"));
        op.Add("Path", new EchoObject(path));
        op.Add("Index", new EchoObject(index));
        operations.ListAdd(op);
    }

    private static void ApplyOperation(EchoObject target, EchoObject operation)
    {
        string opType = operation["Type"].StringValue;
        string path = operation["Path"].StringValue;

        switch (opType)
        {
            case "SetValue":
                ApplySetValue(target, path, operation["Value"]);
                break;
            case "AddCompoundTag":
                ApplyAddCompoundTag(target, path, operation["Key"].StringValue, operation["Value"]);
                break;
            case "RemoveCompoundTag":
                ApplyRemoveCompoundTag(target, path, operation["Key"].StringValue);
                break;
            case "AddListItem":
                ApplyAddListItem(target, path, operation["Index"].IntValue, operation["Value"]);
                break;
            case "RemoveListItem":
                ApplyRemoveListItem(target, path, operation["Index"].IntValue);
                break;
            default:
                throw new InvalidOperationException($"Unknown operation type: {opType}");
        }
    }

    private static void ApplySetValue(EchoObject target, string path, EchoObject value)
    {
        if (string.IsNullOrEmpty(path))
        {
            // Replacing the root - need to copy all properties using SetValue
            // Clear parent first to allow setting
            EchoObject? oldParent = target.Parent;
            string? oldKey = target.CompoundKey;
            int? oldIndex = target.ListIndex;
            target.Parent = null;
            target.CompoundKey = null;
            target.ListIndex = null;

            target.TagType = value.TagType;
            if (value.TagType == EchoType.List)
            {
                target.Value = new List<EchoObject>(value.List.Select(x => x.Clone()));
            }
            else if (value.TagType == EchoType.Compound)
            {
                target.Value = value.Tags.Select(kvp => new KeyValuePair<string, EchoObject>(kvp.Key, kvp.Value.Clone())).ToList();
            }
            else
            {
                target._ref = value._ref;
                target._bits = value._bits;
            }

            // Restore parent relationships
            target.Parent = oldParent;
            target.CompoundKey = oldKey;
            target.ListIndex = oldIndex;
        }
        else
        {
            string parentPath = GetParentPath(path);
            string key = GetLastSegment(path);

            if (string.IsNullOrEmpty(parentPath))
            {
                // Direct child of root
                if (target.TagType == EchoType.Compound)
                {
                    target[key] = value.Clone();
                }
                else if (target.TagType == EchoType.List && int.TryParse(key, out int index))
                {
                    EchoObject cloned = value.Clone();
                    target.List[index] = cloned;
                    cloned.Parent = target;
                    cloned.ListIndex = index;
                }
            }
            else
            {
                EchoObject? parent = NavigatePath(target, parentPath) ?? throw new InvalidOperationException($"Parent path not found: {parentPath}");
                if (parent.TagType == EchoType.Compound)
                {
                    parent[key] = value.Clone();
                }
                else if (parent.TagType == EchoType.List && int.TryParse(key, out int index))
                {
                    EchoObject cloned = value.Clone();
                    parent.List[index] = cloned;
                    cloned.Parent = parent;
                    cloned.ListIndex = index;
                }
            }
        }
    }

    private static void ApplyAddCompoundTag(EchoObject target, string path, string key, EchoObject value)
    {
        EchoObject? compound = (string.IsNullOrEmpty(path) ? target : NavigatePath(target, path)) ?? throw new InvalidOperationException($"Path not found: {path}");
        if (compound.TagType != EchoType.Compound)
            throw new InvalidOperationException($"Target at path '{path}' is not a compound");

        compound[key] = value.Clone();
    }

    private static void ApplyRemoveCompoundTag(EchoObject target, string path, string key)
    {
        EchoObject? compound = (string.IsNullOrEmpty(path) ? target : NavigatePath(target, path)) ?? throw new InvalidOperationException($"Path not found: {path}");
        if (compound.TagType != EchoType.Compound)
            throw new InvalidOperationException($"Target at path '{path}' is not a compound");

        compound.Remove(key);
    }

    private static void ApplyAddListItem(EchoObject target, string path, int index, EchoObject value)
    {
        EchoObject? list = (string.IsNullOrEmpty(path) ? target : NavigatePath(target, path)) ?? throw new InvalidOperationException($"Path not found: {path}");
        if (list.TagType != EchoType.List)
            throw new InvalidOperationException($"Target at path '{path}' is not a list");

        list.ListInsert(index, value.Clone());
    }

    private static void ApplyRemoveListItem(EchoObject target, string path, int index)
    {
        EchoObject? list = (string.IsNullOrEmpty(path) ? target : NavigatePath(target, path)) ?? throw new InvalidOperationException($"Path not found: {path}");
        if (list.TagType != EchoType.List)
            throw new InvalidOperationException($"Target at path '{path}' is not a list");

        list.ListRemoveAt(index);
    }

    private static string GetParentPath(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        return lastSlash == -1 ? "" : path.Substring(0, lastSlash);
    }

    private static string GetLastSegment(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        return UnescapePathSegment(lastSlash == -1 ? path : path.Substring(lastSlash + 1));
    }

    // Path segments are joined by '/', so a key that itself contains '/' (or the escape char '~') is
    // escaped JSON-Pointer style. The '/' separator between segments stays literal.
    private static string EscapePathSegment(string segment) => segment.Replace("~", "~0").Replace("/", "~1");
    private static string UnescapePathSegment(string segment) => segment.Replace("~1", "/").Replace("~0", "~");

    // Walks an escaped '/'-joined path from root, unescaping each segment. Replaces EchoObject.Find here
    // because Find splits on raw '/' and would mis-split a key containing one.
    private static EchoObject? NavigatePath(EchoObject root, string escapedPath)
    {
        if (string.IsNullOrEmpty(escapedPath))
            return root;

        EchoObject? current = root;
        foreach (string segment in escapedPath.Split('/'))
        {
            if (current == null) return null;
            string key = UnescapePathSegment(segment);

            if (current.TagType == EchoType.Compound)
            {
                if (!current.Tags.TryGetValue(key, out current)) return null;
            }
            else if (current.TagType == EchoType.List && int.TryParse(key, out int idx))
            {
                List<EchoObject> list = current.List;
                if (idx < 0 || idx >= list.Count) return null;
                current = list[idx];
            }
            else return null;
        }
        return current;
    }
}
