// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Editor.Build;

/// <summary>
/// A named phase of a build. An open id rather than an enum, because a platform shipped out of tree needs
/// stages this engine has never heard of, and an enum would force those names into this repository.
/// </summary>
public readonly record struct BuildStage(string Id)
{
    public static readonly BuildStage CompileCode = new("compile-code");
    public static readonly BuildStage Validate = new("validate");
    public static readonly BuildStage CompileShaders = new("compile-shaders");
    public static readonly BuildStage ProcessAssets = new("process-assets");
    public static readonly BuildStage ChunkAssets = new("chunk-assets");
    public static readonly BuildStage PackAssets = new("pack-assets");
    public static readonly BuildStage GeneratePlayer = new("generate-player");
    public static readonly BuildStage CompilePlayer = new("compile-player");
    public static readonly BuildStage CopyRuntime = new("copy-runtime");
    public static readonly BuildStage CopyPlugins = new("copy-plugins");
    public static readonly BuildStage WriteManifest = new("write-manifest");
    public static readonly BuildStage ExportSettings = new("export-settings");
    public static readonly BuildStage Package = new("package");
    public static readonly BuildStage Sign = new("sign");
    public static readonly BuildStage Publish = new("publish");
    public static readonly BuildStage Finalize = new("finalize");

    public override string ToString() => Id;
}

/// <summary>
/// Which resource a stage contends for. Texture compression saturates cores while copying saturates a
/// disk, so one global parallelism limit gets both wrong.
/// </summary>
public enum StageResources
{
    CpuBound,
    IoBound,
    Network,

    /// <summary>Runs alone. For steps that own the output directory or an external tool that dislikes company.</summary>
    Exclusive,
}

/// <summary>What happens to the build when an operation in a stage fails.</summary>
public enum StageFailurePolicy
{
    /// <summary>Abandon the build. Correct for code compilation, where nothing later can be meaningful.</summary>
    FailFast,

    /// <summary>Finish the stage and report every failure together. Correct for asset processing.</summary>
    CollectAndContinue,
}
