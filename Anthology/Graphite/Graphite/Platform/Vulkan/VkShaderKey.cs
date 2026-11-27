using System;

namespace Prowl.Graphite.Vk;

/// <summary>Identity of a shader: program key plus resource layouts and push constant block.</summary>
internal sealed class VkShaderKey : IEquatable<VkShaderKey>
{
    private readonly ProgramKey _program;
    private readonly ResourceLayoutDescription[] _layouts;
    private readonly PushConstantRange _pushConstants;
    private readonly int _hash;

    public VkShaderKey(ProgramKey program, ResourceLayoutDescription[] layouts, PushConstantRange pushConstants)
    {
        _program = program;
        _layouts = layouts;
        _pushConstants = pushConstants;

        HashCode hash = new();
        hash.Add(_program);
        hash.Add(_pushConstants);
        foreach (ResourceLayoutDescription layout in layouts)
        {
            hash.Add(layout.Set);
            hash.Add(layout.Table);
            hash.Add(layout.Elements?.ArrayHash() ?? 0);
        }

        _hash = hash.ToHashCode();
    }

    public bool Equals(VkShaderKey? other)
    {
        if (other is null || _hash != other._hash || _program != other._program || _pushConstants != other._pushConstants)
            return false;

        if (_layouts.Length != other._layouts.Length)
            return false;

        for (int i = 0; i < _layouts.Length; i++)
        {
            if (_layouts[i].Set != other._layouts[i].Set
                || _layouts[i].Table != other._layouts[i].Table
                || !Util.ArrayEqualsEquatable(_layouts[i].Elements, other._layouts[i].Elements))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as VkShaderKey);

    public override int GetHashCode() => _hash;
}
