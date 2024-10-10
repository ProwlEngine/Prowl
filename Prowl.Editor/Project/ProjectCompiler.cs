// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;

namespace Prowl.Editor;

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


    private static void Override(XElement node, XElement newNode)
    {
        XElement? existing = node.Element(newNode.Name);
        existing?.Remove();

        node.Add(newNode);
    }


    private static XElement FindOrCreate(XElement parent, XElement node)
    {
        XElement? match = parent.Elements(node.Name)
            .FirstOrDefault(x => node.Attributes().All(y => x.Attribute(y.Name)?.Value == y.Value));

        if (match == null)
        {
            parent.Add(node);
            return node;
        }

        return match;
    }


    public static void GenerateCSProject(
        string assemblyName,
        FileInfo outputFile,
        DirectoryInfo projectPath,
        IEnumerable<FileInfo> scriptPaths,
        IEnumerable<Assembly> references,
        bool allowUnsafe = false,
        bool publishAOT = false,
        bool isPrivate = false,
        DirectoryInfo? outputPath = null,
        DirectoryInfo? tempPath = null
    )
    {
        GenerateCSProject(
            assemblyName,
            outputFile,
            projectPath,
            scriptPaths,
            references.Select(x => (x.GetName().Name!, x.Location)),
            allowUnsafe,
            publishAOT,
            isPrivate,
            outputPath,
            tempPath
        );
    }


    public static void GenerateCSProject(
        string assemblyName,
        FileInfo outputFile,
        DirectoryInfo projectPath,
        IEnumerable<FileInfo> scriptPaths,
        IEnumerable<Assembly> references,
        IEnumerable<(string, string)> rawReferences,
        bool allowUnsafe = false,
        bool publishAOT = false,
        bool isPrivate = false,
        DirectoryInfo? outputPath = null,
        DirectoryInfo? tempPath = null
    )
    {
        GenerateCSProject(
            assemblyName,
            outputFile,
            projectPath,
            scriptPaths,
            references.Select(x => (x.GetName().Name!, x.Location)).Concat(rawReferences),
            allowUnsafe,
            publishAOT,
            isPrivate,
            outputPath,
            tempPath
        );
    }


    public static void GenerateCSProject(
        string assemblyName,
        FileInfo outputFile,
        DirectoryInfo projectPath,
        IEnumerable<FileInfo> scriptPaths,
        IEnumerable<(string, string)> references,
        bool allowUnsafe = false,
        bool publishAOT = false,
        bool isPrivate = false,
        DirectoryInfo? outputPath = null,
        DirectoryInfo? tempPath = null
    )
    {
        Runtime.Debug.Log($"Recreating csproj: {outputFile.FullName}.");

        XDocument projectDocument = outputFile.Exists ? XDocument.Load(outputFile.FullName) : new XDocument();

        XElement? projectXML = projectDocument.Element("Project");

        if (projectXML == null || projectXML.Attribute("Sdk")?.Value != "Microsoft.NET.Sdk")
        {
            projectXML = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"));
            projectDocument.Add(projectXML);
        }

        foreach (XElement toRemove in projectXML.Elements().Where(x => x.Attribute("Label")?.Value == "RemoveFromBuild"))
            toRemove.Remove();

        XElement propertyGroupXML = FindOrCreate(projectXML,
            new XElement("PropertyGroup",
                new XAttribute("Label", "Compile")
            )
        );

        propertyGroupXML.RemoveNodes();

        propertyGroupXML.Add(new XElement("TargetFramework", "net8.0"));
        propertyGroupXML.Add(new XElement("AssemblyName", assemblyName));
        propertyGroupXML.Add(new XElement("ImplicitUsings", "enable"));
        propertyGroupXML.Add(new XElement("Nullable", "enable"));
        propertyGroupXML.Add(new XElement("AllowUnsafeBlocks", allowUnsafe));
        propertyGroupXML.Add(new XElement("PublishAot", publishAOT));
        propertyGroupXML.Add(new XElement("DefaultItemExcludes", "**\\**"));
        propertyGroupXML.Add(new XElement("ProjectPath", projectPath.FullName));

        if (outputPath != null)
            propertyGroupXML.Add(new XElement("OutputPath", outputPath.FullName));

        if (tempPath != null)
            propertyGroupXML.Add(new XElement("BaseIntermediateOutputPath", tempPath.FullName));

        XElement scriptsXML = FindOrCreate(projectXML,
            new XElement("ItemGroup",
                new XAttribute("Label", "Compile")
            )
        );

        scriptsXML.RemoveNodes();

        scriptsXML.Add(
            scriptPaths.Select(x =>
                new XElement("Compile", new XAttribute("Include", $"$(ProjectPath){Path.DirectorySeparatorChar}{Path.GetRelativePath(projectPath.FullName, x.FullName)}"))
            )
        );

        XElement referencesXML = FindOrCreate(projectXML,
            new XElement("ItemGroup",
                new XAttribute("Label", "References")
            )
        );

        referencesXML.RemoveNodes();

        referencesXML.Add(
            references.Select(x => new XElement("Reference",
                new XAttribute("Include", x.Item1),
                new XElement("HintPath", x.Item2),
                new XElement("Private", isPrivate)
            ))
        );

        projectDocument.Save(outputFile.FullName);
    }


    public static bool CompileCSProject(FileInfo project, DirectoryInfo? output, DirectoryInfo? temp, DotnetCompileOptions options)
    {
        if (!CheckForSDKInstallation("8.0"))
            return false;

        Runtime.Debug.Log($"Compiling external assembly in {project.Directory!.Name}...");

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = options.ConstructDotnetArgs(project, output, temp),
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
