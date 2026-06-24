// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Graphite.Variants;

namespace Prowl.Runtime.GUI;

// Shared between Prowl.Runtime and the Tools/CompileUIShaders tool

/// <summary>Top level of a serialized GUI shader blob: every compiled variant permutation.</summary>
public struct UIShaderBlobData
{
    public UIShaderVariantData[] Variants;
}

/// <summary>One compiled variant: its fixed keyword set plus the per-backend reflected descriptions.</summary>
public struct UIShaderVariantData
{
    public Keyword[] Keywords;
    public UIShaderBackendData[] Backends;
}

/// <summary>A reflected <see cref="ShaderDescription"/> for a single backend.</summary>
public struct UIShaderBackendData
{
    public GraphicsBackend Backend;
    public ShaderDescription Description;
}