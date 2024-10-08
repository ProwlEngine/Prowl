// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

using Prowl.Runtime;

namespace Prowl.Editor;

public struct CSCompileOptions()
{
    public bool isRelease = false;
    public bool isSelfContained = false;
    public bool allowUnsafeBlocks = false;

    public Architecture? architecture = null;
    public Platform? platform = null;


    public bool? publishAOT = null;
    public bool? outputExecutable = false;
    public string? startupObject = null;


    public readonly string ConstructDotnetArgs(FileInfo project, DirectoryInfo? outputPath)
    {
        List<string> args = ["build", project.FullName];

        if (outputPath != null)
        {
            args.Add("--output");
            args.Add(outputPath.FullName);
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

        if (publishAOT != null)
        {
            args.Add($"--property:PublishAot={(publishAOT.Value ? "true" : "false")}");
        }

        if (startupObject != null)
        {
            args.Add($"--property:StartupObject={startupObject}");
        }

        if (outputExecutable != null)
        {
            args.Add($"--property:OutputType={(outputExecutable.Value ? "Exe" : "Library")}");
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

/// <summary>
/// Project is a static class that handles all Project related information and actions
/// </summary>
public static class ProjectCompiler
{
    private static bool CheckForSDKInstallation(string sdkVersion)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "dotnet",
                Arguments = "--list-sdks",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = new()
            {
                StartInfo = startInfo
            };

            process.Start();

            List<string> outputLines = [];

            process.OutputDataReceived += (sender, dataArgs) =>
            {
                if (dataArgs.Data != null)
                    outputLines.AddRange(dataArgs.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            };

            process.BeginOutputReadLine();
            process.WaitForExit();

            bool foundSDK = outputLines.Exists(x => x.Contains(sdkVersion));

            if (!foundSDK)
                Runtime.Debug.LogError($"Failed to find SDK of version: {sdkVersion}");

            return foundSDK;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogException(ex);
            return false;
        }
    }


    public static void GenerateCSProject(
        string assemblyName,
        FileInfo outputFile,
        IEnumerable<FileInfo> scriptPaths,
        IEnumerable<Assembly> references,
        bool allowUnsafe = false,
        bool publishAOT = false)
    {
        Runtime.Debug.Log($"Recreating csproj: {outputFile.FullName}.");

        XElement propertyGroupXML = new XElement("PropertyGroup",
            new XElement("TargetFramework", "net8.0"),
            new XElement("AssemblyName", assemblyName),
            new XElement("ImplicitUsings", "enable"),
            new XElement("Nullable", "enable"),
            new XElement("AllowUnsafeBlocks", allowUnsafe),
            new XElement("PublishAot", publishAOT),
            new XElement("DefaultItemExcludes", "**\\**")
        );

        XElement scriptsXML = new XElement("ItemGroup",
            // new XElement("Compile", new XAttribute("Remove", "**/*.cs")),
            scriptPaths.Select(x =>
                new XElement("Compile", new XAttribute("Include", x.FullName))
            )
        );

        XElement referencesXML = new XElement("ItemGroup",
            references.Select(x => new XElement("Reference",
                new XAttribute("Include", x.GetName().Name ?? "Unknown Assembly"),
                new XElement("HintPath", x.Location),
                new XElement("Private", publishAOT)
            ))
        );

        XElement projectXML = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            propertyGroupXML,
            scriptsXML,
            referencesXML
        );

        XDocument projectDocument = new XDocument(projectXML);

        projectDocument.Save(outputFile.FullName);
    }


    public static bool CompileCSProject(FileInfo project, DirectoryInfo? output, CSCompileOptions options)
    {
        if (!CheckForSDKInstallation("8.0"))
            return false;

        Runtime.Debug.Log($"Compiling external assembly in {project.Directory!.Name}...");

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = options.ConstructDotnetArgs(project, output),
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        Process process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        process.OutputDataReceived += (sender, dataArgs) =>
        {
            string? data = dataArgs.Data;

            if (string.IsNullOrWhiteSpace(data))
                return;

            if (data.Contains("Warning", StringComparison.OrdinalIgnoreCase))
                Runtime.Debug.LogWarning(data);
            else if (data.Contains("Error", StringComparison.OrdinalIgnoreCase))
                Runtime.Debug.LogError(data);
            else
                Runtime.Debug.Log(data);
        };

        process.ErrorDataReceived += (sender, dataArgs) =>
        {
            if (dataArgs.Data is not null)
                Runtime.Debug.LogError(dataArgs.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        int exitCode = process.ExitCode;

        process.Close();

        Runtime.Debug.Log($"{(exitCode == 0 ? "Successfully" : "Failed to")} compile external assembly (exit code {exitCode}).");

        return exitCode == 0;
    }


    public static List<Assembly> GetNonstandardReferences(Assembly assembly)
    {
        List<Assembly> nonstandardRefs = [];

        foreach (AssemblyName name in assembly.GetReferencedAssemblies())
        {
            if (!s_defaultCSAssemblyNames.Contains(name.Name))
                nonstandardRefs.Add(Assembly.Load(name));
        }

        return nonstandardRefs;
    }


    // All the assembies included by the .NET SDK when building a self-contained executable
    private static readonly string[] s_defaultCSAssemblyNames =
    [
        "System.Runtime",
        "System.IO.FileSystem.Primitives",
        "System.Net.NetworkInformation",
        "System.Runtime.Intrinsics",
        "System.Security.Cryptography",
        "System.Threading.ThreadPool",
        "System.Diagnostics.Debug",
        "System.Net.Sockets",
        "System.Runtime.InteropServices.JavaScript",
        "System.Net.Ping",
        "System.Net.Mail",
        "System.Diagnostics.DiagnosticSource",
        "System.Reflection.DispatchProxy",
        "System.Private.Xml",
        "mscorlib",
        "Microsoft.VisualBasic",
        "System.IO.FileSystem",
        "System.Net.Security",
        "netstandard",
        "System.Reflection.Primitives",
        "System.Globalization",
        "System.Runtime.InteropServices.RuntimeInformation",
        "System.Formats.Asn1",
        "System.Runtime.Serialization.Json",
        "System.Diagnostics.StackTrace",
        "System.Text.Json",
        "System.Globalization.Extensions",
        "System.Reflection.TypeExtensions",
        "System.Threading.Tasks.Parallel",
        "System.IO.FileSystem.Watcher",
        "System.Threading.Tasks.Dataflow",
        "System.ServiceModel.Web",
        "System.Console",
        "System.ComponentModel.Annotations",
        "System.Threading.Channels",
        "System.Net.Http",
        "System.Reflection.Emit.Lightweight",
        "System.Buffers",
        "System.Security.Cryptography.X509Certificates",
        "System.Net.Quic",
        "System.Reflection.Extensions",
        "System.IO.Pipes.AccessControl",
        "System.ComponentModel.Primitives",
        "System.Security.AccessControl",
        "System.Text.Encoding.CodePages",
        "System.Collections.NonGeneric",
        "System.Net",
        "System.Runtime.Numerics",
        "System.Transactions",
        "System.IO.Compression.ZipFile",
        "System.Text.Encoding",
        "System.Runtime.Serialization.Formatters",
        "System.Dynamic.Runtime",
        "System.Transactions.Local",
        "System.Xml",
        "System.Net.Requests",
        "System.Security.Cryptography.Encoding",
        "Microsoft.VisualBasic.Core",
        "System.Security.Cryptography.Algorithms",
        "System.Net.WebSockets.Client",
        "System.Xml.XDocument",
        "System.Private.Uri",
        "System.Net.ServicePoint",
        "System.Reflection.Emit",
        "System.Net.Http.Json",
        "System.Formats.Tar",
        "System.Runtime.Serialization.Xml",
        "System.Data.Common",
        "System.Net.Primitives",
        "System.Drawing.Primitives",
        "System.Diagnostics.Tracing",
        "System.Xml.Serialization",
        "System.IO.UnmanagedMemoryStream",
        "System.Diagnostics.FileVersionInfo",
        "System.Security.Claims",
        "System.Threading.Overlapped",
        "System.Private.Xml.Linq",
        "System.Data",
        "System.Text.Encoding.Extensions",
        "System.Threading.Timer",
        "System.Collections",
        "System.Linq",
        "System.Collections.Immutable",
        "System.Security.Principal",
        "System.Security.Cryptography.OpenSsl",
        "Microsoft.CSharp",
        "System.IO.MemoryMappedFiles",
        "System.Globalization.Calendars",
        "System.ObjectModel",
        "System.Security.Cryptography.Cng",
        "System.Net.WebSockets",
        "System.Security.Cryptography.Primitives",
        "System.Security",
        "System.Collections.Concurrent",
        "System",
        "System.ComponentModel.TypeConverter",
        "System.ComponentModel",
        "System.Xml.XmlSerializer",
        "System.Diagnostics.Tools",
        "System.Xml.XmlDocument",
        "System.Security.SecureString",
        "System.IO.Compression.Brotli",
        "System.Resources.Writer",
        "System.Diagnostics.Process",
        "System.Linq.Queryable",
        "System.IO.Compression.FileSystem",
        "System.Net.NameResolution",
        "System.Runtime.Handles",
        "System.Resources.ResourceManager",
        "System.Threading.Tasks.Extensions",
        "System.ComponentModel.DataAnnotations",
        "System.Diagnostics.TraceSource",
        "System.Web",
        "System.IO.Pipes",
        "System.Text.Encodings.Web",
        "System.IO",
        "System.Runtime.Extensions",
        "System.Numerics",
        "System.ServiceProcess",
        "System.Text.RegularExpressions",
        "System.Runtime.CompilerServices.VisualC",
        "System.AppContext",
        "System.Linq.Parallel",
        "System.ValueTuple",
        "System.Xml.XPath.XDocument",
        "System.ComponentModel.EventBasedAsync",
        "System.IO.Compression",
        "System.Reflection.Emit.ILGeneration",
        "System.Runtime.Serialization",
        "System.Memory",
        "System.Runtime.InteropServices",
        "System.Reflection",
        "System.Diagnostics.TextWriterTraceListener",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Collections.Specialized",
        "System.Security.Principal.Windows",
        "System.Net.HttpListener",
        "System.Numerics.Vectors",
        "System.Configuration",
        "System.Private.DataContractSerialization",
        "System.IO.IsolatedStorage",
        "WindowsBase",
        "Microsoft.Win32.Registry",
        "System.Drawing",
        "Microsoft.Win32.Primitives",
        "System.Diagnostics.Contracts",
        "System.Reflection.Metadata",
        "System.Xml.Linq",
        "System.Windows",
        "System.Resources.Reader",
        "System.Threading.Tasks",
        "System.Threading",
        "System.Security.Cryptography.Csp",
        "System.IO.FileSystem.DriveInfo",
        "System.Threading.Thread",
        "System.Net.WebProxy",
        "System.Net.WebHeaderCollection",
        "System.Xml.XPath",
        "System.Core",
        "System.Web.HttpUtility",
        "System.Xml.ReaderWriter",
        "System.Runtime.Loader",
        "System.IO.FileSystem.AccessControl",
        "System.Linq.Expressions",
        "System.Data.DataSetExtensions",
        "System.Private.CoreLib",
        "System.Net.WebClient",
        "System.Runtime.Serialization.Primitives",
    ];
}
