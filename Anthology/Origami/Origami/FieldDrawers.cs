// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Built-in field drawers for primitive and math types.
// Registered by the editor at startup via BuiltInFieldDrawers.Register().

using System;

using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

// ── Primitives ──────────────────────────────────────────────

public class BoolDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Switch(paper, id, (bool)(value ?? false), v => onChange(v)).Primary().Show();
}

public class IntDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.NumericField<int>(paper, id, (int)(value ?? 0), v => onChange(v)).Show();
}

public class FloatDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.NumericField<float>(paper, id, (float)(value ?? 0f), v => onChange(v)).Show();
}

public class DoubleDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.NumericField<double>(paper, id, (double)(value ?? 0.0), v => onChange(v)).Show();
}

public class StringDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.TextField(paper, id, (string?)value ?? "", v => onChange(v)).Show();
}

public class ByteDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.NumericField<byte>(paper, id, (byte)(value ?? (byte)0), v => onChange(v)).Min(0).Max(255).Show();
}

public class LongDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.NumericField<long>(paper, id, (long)(value ?? 0L), v => onChange(v)).Show();
}

// ── Vectors ─────────────────────────────────────────────────

public class Float2Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Float2Field(paper, id, (Float2)(value ?? Float2.Zero), v => onChange(v)).Show();
}

public class Float3Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Float3Field(paper, id, (Float3)(value ?? Float3.Zero), v => onChange(v)).Show();
}

public class Float4Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Float4Field(paper, id, (Float4)(value ?? Float4.Zero), v => onChange(v)).Show();
}

public class Double2Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Double2Field(paper, id, (Double2)(value ?? Double2.Zero), v => onChange(v)).Show();
}

public class Double3Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Double3Field(paper, id, (Double3)(value ?? Double3.Zero), v => onChange(v)).Show();
}

public class Double4Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Double4Field(paper, id, (Double4)(value ?? Double4.Zero), v => onChange(v)).Show();
}

public class Int2Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Int2Field(paper, id, (Int2)(value ?? Int2.Zero), v => onChange(v)).Show();
}

public class Int3Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Int3Field(paper, id, (Int3)(value ?? Int3.Zero), v => onChange(v)).Show();
}

public class Int4Drawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.Int4Field(paper, id, (Int4)(value ?? Int4.Zero), v => onChange(v)).Show();
}

// ── Color ───────────────────────────────────────────────────

public class ColorDrawer : FieldDrawer
{
    public override void Draw(Paper paper, string id, object? value, Type fieldType, Action<object?> onChange, int depth)
        => Origami.ColorField(paper, id, (Prowl.Vector.Color)(value ?? new Prowl.Vector.Color(1, 1, 1, 1)), v => onChange(v)).Show();
}

// ── Registration ────────────────────────────────────────────

/// <summary>Registers all built-in field drawers into the given registry.</summary>
public static class BuiltInFieldDrawers
{
    public static void Register(FieldDrawerRegistry registry)
    {
        registry.Register<bool>(new BoolDrawer());
        registry.Register<int>(new IntDrawer());
        registry.Register<float>(new FloatDrawer());
        registry.Register<double>(new DoubleDrawer());
        registry.Register<string>(new StringDrawer());
        registry.Register<byte>(new ByteDrawer());
        registry.Register<long>(new LongDrawer());

        registry.Register<Float2>(new Float2Drawer());
        registry.Register<Float3>(new Float3Drawer());
        registry.Register<Float4>(new Float4Drawer());
        registry.Register<Double2>(new Double2Drawer());
        registry.Register<Double3>(new Double3Drawer());
        registry.Register<Double4>(new Double4Drawer());
        registry.Register<Int2>(new Int2Drawer());
        registry.Register<Int3>(new Int3Drawer());
        registry.Register<Int4>(new Int4Drawer());

        registry.Register<Prowl.Vector.Color>(new ColorDrawer());
    }
}
