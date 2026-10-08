using System;

namespace Prowl.Graphite.Vk;

/// <summary>Identity of a shader: program key plus resource layouts.</summary>
internal sealed class VkShaderKey : IEquatable<VkShaderKey>
{
    private readonly ProgramKey _program;
    private readonly ResourceLayoutDescription[] _layouts;
    private readonly int _hash;

    public VkShaderKey(ProgramKey program, ResourceLayoutDescription[] layouts)
    {
        _program = program;
        _layouts = layouts;

        HashCode hash = new();
        hash.Add(_program);
        foreach (ResourceLayoutDescription layout in layouts)
        {
            hash.Add(layout.Set);
            hash.Add(layout.Elements?.ArrayHash() ?? 0);
        }

        _hash = hash.ToHashCode();
    }

    public bool Equals(VkShaderKey? other)
    {
        if (other is null || _hash != other._hash || _program != other._program)
            return false;

        if (_layouts.Length != other._layouts.Length)
            return false;

        for (int i = 0; i < _layouts.Length; i++)
        {
            if (_layouts[i].Set != other._layouts[i].Set
                || !Util.ArrayEqualsEquatable(_layouts[i].Elements, other._layouts[i].Elements))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as VkShaderKey);

    public override int GetHashCode() => _hash;
}
