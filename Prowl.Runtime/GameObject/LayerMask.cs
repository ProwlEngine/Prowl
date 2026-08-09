// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// A struct that represents a layer mask.
/// <para/>
/// A default-constructed mask matches <b>every</b> layer. That is why the bits stored are exclusions
/// rather than inclusions: a serialized field nobody has touched, or a mask left at <c>default</c>,
/// should let everything through rather than silently matching nothing. The serialized form is still
/// the inclusion mask, so masks saved before this are read back unchanged.
/// </summary>
public struct LayerMask : ISerializable
{
    public readonly static LayerMask Everything = default;
    public readonly static LayerMask Nothing = FromMask(0);

    // Bits set here are the layers NOT matched, so all-zero (the default) matches everything.
    [SerializeField] private uint excluded;

    /// <summary>The layers this mask matches, one bit per layer.</summary>
    public uint Mask => ~excluded;

    /// <summary>Builds a mask from inclusion bits, one per layer.</summary>
    public static LayerMask FromMask(uint mask) => new() { excluded = ~mask };

    public void Clear() => excluded = uint.MaxValue;

    // Use 1u (unsigned) so layer 31 works - a signed 1 << 31 is int.MinValue and sign-extends
    // to long in the comparison, which would make the top layer never match.
    public bool HasLayer(int index) => (excluded & (1u << index)) == 0;
    public void SetLayer(int index) => excluded &= ~(1u << index);
    public void RemoveLayer(int index) => excluded |= 1u << index;

    public static LayerMask operator |(LayerMask mask1, LayerMask mask2) => FromMask(mask1.Mask | mask2.Mask);
    public static LayerMask operator &(LayerMask mask1, LayerMask mask2) => FromMask(mask1.Mask & mask2.Mask);

    public override bool Equals(object? obj)
    {
        if (obj is null || !(obj is LayerMask other))
            return false;
        return excluded == other.excluded;
    }
    public override int GetHashCode() => excluded.GetHashCode();

    public static bool operator ==(LayerMask left, LayerMask right) => left.Equals(right);
    public static bool operator !=(LayerMask left, LayerMask right) => !left.Equals(right);

    // Serialized as the inclusion mask under its original name, so masks written before the storage
    // was inverted still load correctly. Only the in-memory representation changed.
    public void Serialize(ref EchoObject value, SerializationContext ctx) => value.Add("mask", new EchoObject(Mask));

    public void Deserialize(EchoObject value, SerializationContext ctx)
        => excluded = value.TryGet("mask", out EchoObject? mask) ? ~mask!.UIntValue : 0u;

    public static string LayerToName(int index) => TagLayerManager.GetLayer(index);
    public static int NameToLayer(string name) => TagLayerManager.GetLayerIndex(name);
}
