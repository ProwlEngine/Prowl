// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GraphTools.ShaderGraphs;

/// <summary>Which generated stage a piece of code belongs in. Used by the editor's
/// shader compiler when traversing a graph; lives in Runtime so node types can branch
/// on it without taking an editor dependency.</summary>
public enum ShaderStage { Vertex, Fragment }
