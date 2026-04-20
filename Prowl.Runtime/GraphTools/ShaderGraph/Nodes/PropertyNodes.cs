// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Marker for nodes that emit a material-bindable shader uniform. Compiler collects
/// every <see cref="IShaderProperty"/> in the graph, emits Properties{} block entries
/// + uniform decls, and the Material inspector binds the user's edits to those uniforms.
/// </summary>
public interface IShaderProperty
{
    /// <summary>Identifier emitted as the GLSL uniform name (e.g. <c>_MainColor</c>).
    /// Auto-prefixed with `_` if the user omits one. Must be unique within the graph.</summary>
    string PropertyName { get; }

    /// <summary>Display name shown in the material inspector.</summary>
    string DisplayName { get; }

    /// <summary>True = appears in the material inspector. False = uniform is still
    /// emitted so the graph can use it (e.g. set from C# via Material.SetTexture)
    /// but it stays out of the inspector UI.</summary>
    bool Exposed { get; }

    /// <summary>The shader-side type — drives the Properties{} keyword + uniform decl.</summary>
    ShaderType PropertyType { get; }

    /// <summary>Default value as it would appear in the Properties{} block (e.g.
    /// `(1.0, 1.0, 1.0, 1.0)` for a Color or `"white"` for a Texture2D).</summary>
    string DefaultLiteral { get; }
}

/// <summary>Material-bindable color property. Output port emits the uniform's GLSL name.</summary>
public sealed class ColorPropertyNode : Node, IShaderGraphNode, IShaderProperty
{
    public string Name = "_MainColor";
    public string Label = "Color";
    public Color Value = new Color(1, 1, 1, 1);
    public bool ExposedToInspector = true;

    public override string Title => $"Color · {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 60, 130, 200);

    string IShaderProperty.PropertyName => NormaliseName(Name);
    string IShaderProperty.DisplayName  => string.IsNullOrEmpty(Label) ? Name : Label;
    bool   IShaderProperty.Exposed      => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType => ShaderType.Color;
    string IShaderProperty.DefaultLiteral
        => $"({F(Value.R)}, {F(Value.G)}, {F(Value.B)}, {F(Value.A)})";

    protected override void DefineNode() => AddOutput<Color>("Out");

    internal static string NormaliseName(string n) => string.IsNullOrEmpty(n) ? "_Property" : (n.StartsWith("_") ? n : "_" + n);
    internal static string F(double v) => v.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Material-bindable float property.</summary>
public sealed class FloatPropertyNode : Node, IShaderGraphNode, IShaderProperty
{
    public string Name = "_Value";
    public string Label = "Value";
    public float Value = 0.5f;
    public bool ExposedToInspector = true;

    public override string Title => $"Float · {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 60, 130, 200);

    string IShaderProperty.PropertyName => ColorPropertyNode.NormaliseName(Name);
    string IShaderProperty.DisplayName  => string.IsNullOrEmpty(Label) ? Name : Label;
    bool   IShaderProperty.Exposed      => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType => ShaderType.Float;
    string IShaderProperty.DefaultLiteral   => ColorPropertyNode.F(Value);

    protected override void DefineNode() => AddOutput<float>("Out");
}

/// <summary>Material-bindable texture property. Output port emits the sampler name —
/// downstream Tex2D node samples through it.</summary>
public sealed class Texture2DPropertyNode : Node, IShaderGraphNode, IShaderProperty
{
    public string Name = "_MainTex";
    public string Label = "Texture";
    /// <summary>Default texture key — Prowl matches a built-in (e.g. "white", "grid",
    /// "normal") or treats it as an asset path. Mirrors how Standard.shader's
    /// `_MainTex ("Albedo", Texture2D) = "white"` works.</summary>
    public string DefaultTextureName = "white";
    public bool ExposedToInspector = true;

    public override string Title => $"Texture · {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => System.Drawing.Color.FromArgb(255, 60, 130, 200);

    string IShaderProperty.PropertyName => ColorPropertyNode.NormaliseName(Name);
    string IShaderProperty.DisplayName  => string.IsNullOrEmpty(Label) ? Name : Label;
    bool   IShaderProperty.Exposed      => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType => ShaderType.Sampler2D;
    string IShaderProperty.DefaultLiteral   => $"\"{DefaultTextureName}\"";

    protected override void DefineNode() => AddOutput<Resources.Texture2D>("Out");
}
