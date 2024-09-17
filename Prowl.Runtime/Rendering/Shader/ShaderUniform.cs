// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using System.Text;

using Veldrid;

namespace Prowl.Runtime;

public enum ValueType : byte
{
    None = 0,
    Float,
    Int,
    UInt
}

public struct ShaderUniformMember
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

public class ShaderUniform
{
    private static string CleanseName(string rawName)
    {
        return rawName.Replace("type.", "");
    }

    public readonly ResourceKind kind;
    public readonly string name;
    public readonly uint binding;

    public readonly uint size;

    public readonly ShaderUniformMember[] members;


    private ShaderUniform() { }

    public ShaderUniform(string rawName, uint binding, ResourceKind kind)
    {
        this.kind = kind;
        name = CleanseName(rawName);
        this.binding = binding;
        members = [];
    }


    public ShaderUniform(string rawName, uint binding, uint size, ShaderUniformMember[] members)
    {
        kind = ResourceKind.UniformBuffer;
        name = CleanseName(rawName);
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


    public bool IsEqual(ShaderUniform other)
    {
        if (kind != other.kind)
            return false;

        if (kind == ResourceKind.UniformBuffer && size != other.size && !members.SequenceEqual(other.members))
            return false;

        return name == other.name && binding == other.binding;
    }
}
