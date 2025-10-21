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
        var delta = NewCompound();
        var operations = NewList();
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
        var result = baseline.Clone();

        if (!delta.TryGet("Operations", out var operationsList))
            throw new ArgumentException("Delta does not contain Operations list", nameof(delta));

        foreach (var operation in operationsList!.List)
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
        var fromTags = from.Tags;
        var toTags = to.Tags;

        // Find removed keys
        foreach (var key in fromTags.Keys)
        {
            if (!toTags.ContainsKey(key))
            {
                AddRemoveCompoundTagOperation(operations, path, key);
            }
        }

        // Find added or changed keys
        foreach (var (key, toValue) in toTags)
        {
            string childPath = string.IsNullOrEmpty(path) ? key : $"{path}/{key}";

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
        var fromList = from.List;
        var toList = to.List;

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
        var op = NewCompound();
        op.Add("Type", new EchoObject("SetValue"));
        op.Add("Path", new EchoObject(path));
        op.Add("Value", value.Clone());
        operations.ListAdd(op);
    }

    private static void AddAddCompoundTagOperation(EchoObject operations, string path, string key, EchoObject value)
    {
        var op = NewCompound();
        op.Add("Type", new EchoObject("AddCompoundTag"));
        op.Add("Path", new EchoObject(path));
        op.Add("Key", new EchoObject(key));
        op.Add("Value", value.Clone());
        operations.ListAdd(op);
    }

    private static void AddRemoveCompoundTagOperation(EchoObject operations, string path, string key)
    {
        var op = NewCompound();
        op.Add("Type", new EchoObject("RemoveCompoundTag"));
        op.Add("Path", new EchoObject(path));
        op.Add("Key", new EchoObject(key));
        operations.ListAdd(op);
    }

    private static void AddAddListItemOperation(EchoObject operations, string path, int index, EchoObject value)
    {
        var op = NewCompound();
        op.Add("Type", new EchoObject("AddListItem"));
        op.Add("Path", new EchoObject(path));
        op.Add("Index", new EchoObject(index));
        op.Add("Value", value.Clone());
        operations.ListAdd(op);
    }

    private static void AddRemoveListItemOperation(EchoObject operations, string path, int index)
    {
        var op = NewCompound();
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
            var oldParent = target.Parent;
            var oldKey = target.CompoundKey;
            var oldIndex = target.ListIndex;
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
                target.Value = new Dictionary<string, EchoObject>(
                    value.Tags.Select(kvp => new KeyValuePair<string, EchoObject>(kvp.Key, kvp.Value.Clone())));
            }
            else if (value.TagType == EchoType.Null || value.Value == null)
            {
                // For null values, we need to use reflection to set the private _value field directly
                var valueField = typeof(EchoObject).GetField("_value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                valueField?.SetValue(target, null);
            }
            else
            {
                target.Value = value.Value;
            }

            // Restore parent relationships
            target.Parent = oldParent;
            target.CompoundKey = oldKey;
            target.ListIndex = oldIndex;
        }
        else
        {
            var parentPath = GetParentPath(path);
            var key = GetLastSegment(path);

            if (string.IsNullOrEmpty(parentPath))
            {
                // Direct child of root
                if (target.TagType == EchoType.Compound)
                {
                    var cloned = value.Clone();
                    var dict = target.Tags;
                    if (dict.ContainsKey(key))
                    {
                        var old = dict[key];
                        old.Parent = null;
                        old.CompoundKey = null;
                    }
                    dict[key] = cloned;
                    cloned.Parent = target;
                    cloned.CompoundKey = key;
                }
                else if (target.TagType == EchoType.List && int.TryParse(key, out int index))
                {
                    var cloned = value.Clone();
                    target.List[index] = cloned;
                    cloned.Parent = target;
                    cloned.ListIndex = index;
                }
            }
            else
            {
                var parent = target.Find(parentPath);
                if (parent == null)
                    throw new InvalidOperationException($"Parent path not found: {parentPath}");

                if (parent.TagType == EchoType.Compound)
                {
                    var cloned = value.Clone();
                    var dict = parent.Tags;
                    if (dict.ContainsKey(key))
                    {
                        var old = dict[key];
                        old.Parent = null;
                        old.CompoundKey = null;
                    }
                    dict[key] = cloned;
                    cloned.Parent = parent;
                    cloned.CompoundKey = key;
                }
                else if (parent.TagType == EchoType.List && int.TryParse(key, out int index))
                {
                    var cloned = value.Clone();
                    parent.List[index] = cloned;
                    cloned.Parent = parent;
                    cloned.ListIndex = index;
                }
            }
        }
    }

    private static void ApplyAddCompoundTag(EchoObject target, string path, string key, EchoObject value)
    {
        var compound = string.IsNullOrEmpty(path) ? target : target.Find(path);
        if (compound == null)
            throw new InvalidOperationException($"Path not found: {path}");
        if (compound.TagType != EchoType.Compound)
            throw new InvalidOperationException($"Target at path '{path}' is not a compound");

        var cloned = value.Clone();
        var dict = compound.Tags;

        // If key already exists, clean up old one
        if (dict.ContainsKey(key))
        {
            var old = dict[key];
            old.Parent = null;
            old.CompoundKey = null;
        }

        dict[key] = cloned;
        cloned.Parent = compound;
        cloned.CompoundKey = key;
    }

    private static void ApplyRemoveCompoundTag(EchoObject target, string path, string key)
    {
        var compound = string.IsNullOrEmpty(path) ? target : target.Find(path);
        if (compound == null)
            throw new InvalidOperationException($"Path not found: {path}");
        if (compound.TagType != EchoType.Compound)
            throw new InvalidOperationException($"Target at path '{path}' is not a compound");

        var dict = compound.Tags;
        if (dict.TryGetValue(key, out var tag))
        {
            dict.Remove(key);
            tag.Parent = null;
            tag.CompoundKey = null;
        }
    }

    private static void ApplyAddListItem(EchoObject target, string path, int index, EchoObject value)
    {
        var list = string.IsNullOrEmpty(path) ? target : target.Find(path);
        if (list == null)
            throw new InvalidOperationException($"Path not found: {path}");
        if (list.TagType != EchoType.List)
            throw new InvalidOperationException($"Target at path '{path}' is not a list");

        var clonedValue = value.Clone();
        list.List.Insert(index, clonedValue);
        clonedValue.Parent = list;

        // Update indices for all items from insertion point
        for (int i = index; i < list.List.Count; i++)
        {
            list.List[i].ListIndex = i;
        }
    }

    private static void ApplyRemoveListItem(EchoObject target, string path, int index)
    {
        var list = string.IsNullOrEmpty(path) ? target : target.Find(path);
        if (list == null)
            throw new InvalidOperationException($"Path not found: {path}");
        if (list.TagType != EchoType.List)
            throw new InvalidOperationException($"Target at path '{path}' is not a list");

        var item = list.List[index];
        list.List.RemoveAt(index);
        item.Parent = null;
        item.ListIndex = null;

        // Update indices for all items after removal point
        for (int i = index; i < list.List.Count; i++)
        {
            list.List[i].ListIndex = i;
        }
    }

    private static string GetParentPath(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        return lastSlash == -1 ? "" : path.Substring(0, lastSlash);
    }

    private static string GetLastSegment(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        return lastSlash == -1 ? path : path.Substring(lastSlash + 1);
    }
}
