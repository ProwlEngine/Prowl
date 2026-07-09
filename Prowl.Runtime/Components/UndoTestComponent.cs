// Test component for verifying undo/redo across all widget types.
// Add to a GameObject, edit fields in the inspector, then Ctrl+Z / Ctrl+Y to verify.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public enum TestEnum
{
    OptionA,
    OptionB,
    OptionC,
    OptionD,
}

[Serializable]
public class NestedTestData
{
    public string Label = "Nested";
    public float Value = 1.0f;
    public bool Flag = false;
}

[AddComponentMenu("Testing/Undo Test Component")]
public class UndoTestComponent : MonoBehaviour
{
    // ---- Numeric Primitives ----
    [Header("Numeric Primitives")]
    public bool BoolField = false;
    public int IntField = 42;
    public float FloatField = 3.14f;
    public double DoubleField = 2.718;
    public byte ByteField = 128;
    public short ShortField = -100;
    public ushort UShortField = 200;
    public long LongField = 999999;
    public uint UIntField = 50000;

    // ---- Range Sliders ----
    [Header("Range Sliders")]
    [Range(0f, 100f)]
    public float RangeFloat = 50f;

    [Range(0, 10)]
    public int RangeInt = 5;

    // ---- Strings ----
    [Header("Strings")]
    public string StringField = "Hello World";

    // ---- Math Types ----
    [Header("Math Types")]
    public Float2 Vector2Field = new Float2(1, 2);
    public Float3 Vector3Field = new Float3(1, 2, 3);
    public Float4 Vector4Field = new Float4(1, 2, 3, 4);
    public Quaternion QuaternionField = Quaternion.Identity;
    public Color ColorField = new Color(1, 0, 0, 1);

    // ---- Curves & Gradients ----
    [Header("Curves & Gradients")]
    public AnimationCurve CurveField = new AnimationCurve();
    public Gradient GradientField = new Gradient();
    public MinMaxCurve MinMaxCurveField = new MinMaxCurve(1.0f);

    // ---- Enum ----
    [Header("Enum")]
    public TestEnum EnumField = TestEnum.OptionA;

    // ---- Asset References ----
    [Header("Asset References")]
    public AssetRef<Mesh> MeshRef;
    public AssetRef<Material> MaterialRef;
    public AssetRef<Texture2D> TextureRef;

    // ---- Collections ----
    [Header("Collections")]
    public List<float> FloatList = new() { 1.0f, 2.0f, 3.0f };
    public List<string> StringList = new() { "Alpha", "Beta", "Gamma" };
    public int[] IntArray = { 10, 20, 30 };

    // ---- Nested Object ----
    [Header("Nested Object")]
    public NestedTestData NestedData = new();
}
