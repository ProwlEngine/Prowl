// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using System.Text;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public enum ValueType : byte
{
    None = 0,
    Float,
    Int,
    UInt
}

public struct UniformMember
{
    public string name;

    public uint bufferOffsetInBytes;

    public uint width;
    public uint height;
    public uint size;
    public uint arrayStride;
    public uint matrixStride;

    public ValueType type;
}

public class Uniform
{
    public readonly ResourceKind kind;
    public readonly string name;
    public readonly uint binding;

    public uint size;

    public UniformMember[] members;


    private Uniform() { }

    public Uniform(string name, uint binding, ResourceKind kind)
    {
        this.kind = kind;
        this.name = name;
        this.binding = binding;
        members = [];
    }


    public Uniform(string name, uint binding, uint size, UniformMember[] members)
    {
        kind = ResourceKind.UniformBuffer;
        this.name = name;
        this.binding = binding;
        this.size = size;
        this.members = members;
    }


    public override string ToString()
    {
        StringBuilder sb = new();

        sb.AppendLine($"{name}");
        sb.AppendLine($"  Type: {kind}");
        sb.AppendLine($"  Binding: {binding}");

        if (kind != ResourceKind.UniformBuffer)
            return sb.ToString();

        sb.AppendLine($"  Byte size: {size}");

        if (members == null)
            return sb.ToString();

        sb.AppendLine("  Members:");

        foreach (var member in members)
        {
            sb.AppendLine($"    {member.name}");
            sb.AppendLine($"      Type: {member.type}");
            sb.AppendLine($"      Width: {member.width}");
            sb.AppendLine($"      Height: {member.height}");
            sb.AppendLine($"      Buffer offset: {member.bufferOffsetInBytes}");
            sb.AppendLine($"      Size: {member.size}");
            sb.AppendLine($"      Array stride: {member.arrayStride}");
            sb.AppendLine($"      Matrix stride: {member.matrixStride}");
        }

        return sb.ToString();
    }


    public bool IsEqual(Uniform other)
    {
        if (kind != other.kind)
            return false;

        if (kind == ResourceKind.UniformBuffer && size != other.size && !members.SequenceEqual(other.members))
            return false;

        return name == other.name && binding == other.binding;
    }
}
