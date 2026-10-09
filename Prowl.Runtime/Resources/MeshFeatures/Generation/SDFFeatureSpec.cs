// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.MeshFeatures.Generation;

/// <summary>
/// Registers the SDF mesh feature, generated for every mesh with the project wide settings below.
/// Auto-discovered by <see cref="MeshFeatureRegistry"/> via reflection.
/// </summary>
public sealed class SDFFeatureSpec : MeshFeatureSpec
{
    private const int GeneratorVersion = 2;

    /// <summary>Whether meshes get an SDF. Set from the project's asset settings.</summary>
    public static bool Enabled = false;

    /// <summary>How every mesh's SDF is built. Set from the project's asset settings.</summary>
    public static SDFGenerator.Options Options = SDFGenerator.Options.Default;

    public override string Key => "sdf";
    public override string DisplayName => "Signed Distance Field";
    public override Type FeatureType => typeof(MeshSDF);

    // The settings are part of the version, so changing them reimports every mesh
    public override int Version => Enabled
        ? unchecked(GeneratorVersion + BitConverter.SingleToInt32Bits(Options.VoxelSize) * 31 + Options.MaxResolution * 7919)
        : 1;

    public override void PopulateDefaults(EchoObject settings) { }

    public override Asset? TryGenerate(Mesh mesh, EchoObject? settings) => Enabled ? SDFGenerator.Generate(mesh, Options) : null;
}
