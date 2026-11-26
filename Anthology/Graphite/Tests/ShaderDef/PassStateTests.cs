using System;
using System.Reflection;

using Prowl.Graphite.ShaderDef.Compiler;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;

public class PassStateTests
{
    [Fact]
    public void InvalidNumber_ReportsLineAndColumn()
    {
        // A hex literal is a valid Number token but not a valid float, and it sits on line 2,
        // so the diagnostic must point there rather than line 1.
        ParseException ex = Assert.Throws<ParseException>(() => Parse.State("""
            Cull Back
            Offset 0xFF 0
            """));

        Assert.Equal(2, ex.Line);
        Assert.Contains("number", ex.Message);
    }

    [Fact]
    public void Equals_IgnoresValuesOutsideTheSetMask()
    {
        PassState a = new() { Set = PassStateFields.CullMode, Raster = new() { CullMode = FaceCullMode.Front, DepthBiasEnabled = true } };
        PassState b = new() { Set = PassStateFields.CullMode, Raster = new() { CullMode = FaceCullMode.Front }, Blend = new() };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EveryFlag_IsCopiedByApplyAndCompared()
    {
        PassState source = new()
        {
            Raster = (RasterizerStateDescription)Fill(typeof(RasterizerStateDescription)),
            DepthStencil = (DepthStencilStateDescription)Fill(typeof(DepthStencilStateDescription)),
            Blend = (BlendAttachmentDescription)Fill(typeof(BlendAttachmentDescription)),
            AlphaToCoverage = true,
        };

        foreach (PassStateFields flag in Enum.GetValues<PassStateFields>())
        {
            if (flag == PassStateFields.None)
                continue;

            source.Set = flag;
            PassState applied = new PassState().Apply(source);
            PassState flagOnly = new() { Set = flag };

            Assert.False(flagOnly.Equals(applied), $"{flag} is not copied by Apply or not compared by Equals.");
        }
    }

    private static object Fill(Type type)
    {
        if (type == typeof(bool))
            return true;
        if (type == typeof(float))
            return 1.5f;
        if (type == typeof(byte))
            return (byte)7;
        if (type == typeof(uint))
            return 7u;
        if (type.IsEnum)
        {
            foreach (object value in Enum.GetValues(type))
            {
                if (Convert.ToInt64(value) != 0)
                    return value;
            }
        }
        if (type.IsValueType && !type.IsEnum)
        {
            object instance = Activator.CreateInstance(type)!;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(instance, Fill(field.FieldType));
            return instance;
        }

        throw new NotSupportedException(type.Name);
    }
}
