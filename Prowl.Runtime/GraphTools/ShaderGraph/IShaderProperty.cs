// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Marker for nodes that emit a material-bindable shader uniform. The compiler
/// collects every <see cref="IShaderProperty"/> in the graph, emits Properties{} block
/// entries + uniform decls, and the Material inspector binds the user's edits to those
/// uniforms. Property nodes themselves live in <c>PropertyNodes.cs</c>.
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

/// <summary>
/// Supplementary interface implemented by Float property nodes that want their
/// inspector field to render as a slider between a min/max pair — the Prowl shader
/// parser picks this up via the <c>Range(min, max)</c> type keyword.
/// </summary>
public interface IShaderPropertyRange
{
    /// <summary>Lower bound of the inspector slider.</summary>
    float RangeMin { get; }
    /// <summary>Upper bound of the inspector slider.</summary>
    float RangeMax { get; }
}
