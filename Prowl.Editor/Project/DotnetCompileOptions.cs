// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.InteropServices;

using Prowl.Runtime;

namespace Prowl.Editor;

public struct DotnetCompileOptions()
{
    public bool isRelease = false;
    public bool isSelfContained = false;

    public Architecture? architecture = null;
    public Platform? platform = null;

    public DirectoryInfo? outputPath = null;
    public DirectoryInfo? tempPath = null;


    public string ConstructDotnetArgs(FileInfo project)
    {
        List<string> args = ["build", project.FullName];

        if (outputPath != null)
        {
            args.Add("--output");
            args.Add(outputPath.FullName);
        }

        if (tempPath != null)
        {
            args.Add($"--property:BaseIntermediateOutputPath=\"{tempPath.FullName}\"{Path.DirectorySeparatorChar}");
        }

        args.Add("--configuration");
        args.Add(isRelease ? "Release" : "Debug");

        if (isSelfContained)
            args.Add("--self-contained");

        if (architecture != null)
        {
            args.Add("--arch");

            args.Add(architecture switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x64",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                Architecture.LoongArch64 => "loongarch64",
                Architecture.Wasm => "wasm",
                Architecture.S390x => "s390x",
                Architecture.Ppc64le => "ppc64le",
                Architecture.Armv6 => "armv6",
                _ => throw new Exception($"Unknown target architecture: {architecture}")
            });
        }

        if (platform != null)
        {
            args.Add("--os");

            args.Add(platform switch
            {
                Platform.Android => "android",
                Platform.Browser => "browser",
                Platform.FreeBSD => "freebsd",
                Platform.Haiku => "haiku",
                Platform.Illumos => "illumos",
                Platform.iOS => "ios",
                Platform.iOSSimulator => "iossimulator",
                Platform.Linux => "linux",
                Platform.MacCatalyst => "maccatalyst",
                Platform.MacOS => "osx",
                Platform.Solaris => "solaris",
                Platform.tvOS => "tvos",
                Platform.tvOSSimulator => "tvossimulator",
                Platform.Unix => "unix",
                Platform.Wasi => "wasi",
                Platform.Windows => "win",
                _ => throw new Exception($"Unknown target platform: {platform}")
            });
        }

        return string.Join(" ", args);
    }
}
