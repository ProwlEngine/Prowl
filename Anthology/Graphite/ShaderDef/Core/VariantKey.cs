namespace Prowl.Graphite.ShaderDef;


/// <summary>One variant of a pass, from <see cref="ShaderPass.GetKey"/>. The default key is every axis at its first value.</summary>
public readonly record struct VariantKey
{
    internal VariantKey(int index) => Index = index;

    internal int Index { get; }

    public override string ToString() => $"VariantKey({Index})";
}
