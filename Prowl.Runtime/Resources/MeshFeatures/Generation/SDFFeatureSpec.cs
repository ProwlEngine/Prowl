// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.MeshFeatures.Generation;

/// <summary>
/// Registers the SDF mesh feature: reads settings, invokes <see cref="SDFGenerator"/>.
/// Auto-discovered by <see cref="MeshFeatureRegistry"/> via reflection.
/// </summary>
public sealed class SDFFeatureSpec : MeshFeatureSpec
{
    public const string KeyRoot = "sdf";
    public const string Key_Enabled = "enabled";
    public const string Key_Resolution = "resolution";
    public const string Key_Padding = "padding";
    public const string Key_MaxDistance = "maxDistance";

    public override string Key => KeyRoot;
    public override string DisplayName => "Signed Distance Field";
    public override Type FeatureType => typeof(MeshSDF);
    public override int Version => 1;

    public override void PopulateDefaults(EchoObject settings)
    {
        var sdf = EchoObject.NewCompound();
        sdf[Key_Enabled] = new EchoObject(false);
        sdf[Key_Resolution] = new EchoObject(64);
        sdf[Key_Padding] = new EchoObject(0.1f);
        sdf[Key_MaxDistance] = new EchoObject(0.25f);
        settings[Key] = sdf;
    }

    public override EngineObject? TryGenerate(Mesh mesh, EchoObject? settings)
    {
        var options = ReadOptions(settings, out bool enabled);
        if (!enabled) return null;
        return SDFGenerator.Generate(mesh, options);
    }

    private static SDFGenerator.Options ReadOptions(EchoObject? settings, out bool enabled)
    {
        var opts = SDFGenerator.Options.Default;
        enabled = false;
        if (settings == null) return opts;
        if (!settings.TryGet(KeyRoot, out var sdf) || sdf == null) return opts;

        if (sdf.TryGet(Key_Enabled, out var e)) enabled = e.BoolValue;
        if (sdf.TryGet(Key_Resolution, out var r)) opts.Resolution = r.IntValue;
        if (sdf.TryGet(Key_Padding, out var p)) opts.PaddingFraction = p.FloatValue;
        if (sdf.TryGet(Key_MaxDistance, out var m)) opts.MaxDistanceFraction = m.FloatValue;
        return opts;
    }
}
