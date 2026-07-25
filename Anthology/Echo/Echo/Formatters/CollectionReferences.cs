// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo.Formatters;

// Reference tracking for collections, mirroring AnyObjectFormat. Without it a collection shared by two
// fields deserializes into two copies, and a self-referential collection recurses forever on serialize.
// Uses the same id maps on SerializationContext, so collection and object references share one id space.
internal static class CollectionReferences
{
    // Serialize: returns the { "$id": N } stub if the value was already written, otherwise registers a
    // fresh id (out) and returns null so the caller serializes the body.
    public static EchoObject? TryWriteReference(object value, SerializationContext ctx, out int id)
    {
        if (ctx.objectToId.TryGetValue(value, out id))
        {
            var stub = EchoObject.NewCompound();
            stub["$id"] = new EchoObject(EchoType.Int, id);
            return stub;
        }
        id = ctx.nextId++;
        ctx.objectToId[value] = id;
        ctx.idToObject[id] = value;
        return null;
    }

    // Wraps a bare-list body so it can carry an id: { "$id": N, "$values": [...] }.
    public static EchoObject WrapListBody(EchoObject listBody, int id)
    {
        var compound = EchoObject.NewCompound();
        compound["$id"] = new EchoObject(EchoType.Int, id);
        compound["$values"] = listBody;
        return compound;
    }

    // Adds an id to a collection whose body is already a compound (arrays, etc.).
    public static EchoObject WrapCompoundBody(EchoObject compoundBody, int id)
    {
        compoundBody["$id"] = new EchoObject(EchoType.Int, id);
        return compoundBody;
    }

    // Deserialize: true if `value` is a bare { "$id": N } reference to an already-created instance.
    public static bool TryReadReference(EchoObject value, SerializationContext ctx, out object? existing)
    {
        existing = null;
        if (value.TagType != EchoType.Compound || !value.TryGet("$id", out var idTag))
            return false;
        if (HasBody(value))
            return false;
        return ctx.idToObject.TryGetValue(idTag.IntValue, out existing);
    }

    // Registers a freshly-created collection under its id before its elements are filled, so a reference
    // to it from inside itself resolves to this same instance.
    public static void Register(EchoObject value, object instance, SerializationContext ctx)
    {
        if (value.TagType == EchoType.Compound && value.TryGet("$id", out var idTag))
        {
            ctx.idToObject[idTag.IntValue] = instance;
            ctx.fullyDefinedIds.Add(idTag.IntValue);
        }
    }

    // Element tags for a list-like collection: the new form nests them under "$values", the old
    // pre-wrapper form is a bare list tag.
    public static List<EchoObject> ListItems(EchoObject value)
    {
        if (value.TagType == EchoType.Compound && value.TryGet("$values", out var vals))
            return vals.List;
        return value.List;
    }

    private static bool HasBody(EchoObject compound)
    {
        foreach (var key in compound.Tags.Keys)
            if (key != "$id" && key != "$type")
                return true;
        return false;
    }
}
