// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;
using System.Xml.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Prowl.Runtime;

namespace Prowl.Editor;

public struct CSProjectOptions()
{
    public bool OutputExecutable = false;
    public string? OutputName = null;

    private Dictionary<string, string> _references = [];
    public IEnumerable<KeyValuePair<string, string>> AssemblyReferences => _references;

    public bool AllowUnsafeCode = false;
    public bool EnableAOTCompatibility = false;
    public bool ReferencesArePrivate = false;
    public bool PublishAOT = false;

    public DirectoryInfo? OutputPath = null;
    public DirectoryInfo? IntermediateOutputPath = null;


    public readonly void AddReference(string assemblyPath)
    {
        using (var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var peReader = new PEReader(stream))
        {
            if (!peReader.HasMetadata)
            {
                Debug.LogWarning($"Could not get metadata for file (at {assemblyPath})");
                return;
            }

            MetadataReader metadataReader = peReader.GetMetadataReader();

            if (!metadataReader.IsAssembly)
            {
                Debug.LogWarning($"Could not get assembly definition from metadata in file {assemblyPath}");
                return;
            }

            AssemblyDefinition definition = metadataReader.GetAssemblyDefinition();
            string name = metadataReader.GetString(definition.Name);

            _references[name] = assemblyPath;
        }
    }

    public readonly void AddReference(Assembly assembly, bool resolveNonstandardReferences)
    {
        _references[assembly.GetName().Name!] = assembly.Location;

        if (resolveNonstandardReferences)
        {
            foreach (Assembly reference in GetNonstandardReferences(assembly))
                AddReference(reference, false);
        }
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


    public void GenerateCSProject(
        FileInfo projectFile,
        DirectoryInfo scriptRoot,
        IEnumerable<FileInfo> scriptPaths)
    {
        XDocument projectDocument = projectFile.Exists ? XDocument.Load(projectFile.FullName) : new XDocument();

        XElement? projectXML = projectDocument.Element("Project");

        if (projectXML == null || projectXML.Attribute("Sdk")?.Value != "Microsoft.NET.Sdk")
        {
            projectXML = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"));
            projectDocument.Add(projectXML);
        }

        foreach (XElement element in projectXML.Elements().ToList())
        {
            if (element.Attributes("Label").Any(x => x.Value == "RemoveFromBuild"))
                element.Remove();
        }

        XElement propertyGroupXML = FindOrCreate(projectXML,
            new XElement("PropertyGroup",
                new XAttribute("Label", "Compile")
            )
        );

        propertyGroupXML.RemoveNodes();

        propertyGroupXML.Add(new XElement("TargetFramework", "net9.0"));

        propertyGroupXML.Add(new XElement("OutputType", OutputExecutable ? "Exe" : "Library"));

        if (OutputName != null)
            propertyGroupXML.Add(new XElement("AssemblyName", OutputName));

        propertyGroupXML.Add(new XElement("AllowUnsafeBlocks", AllowUnsafeCode));

        if (EnableAOTCompatibility)
            propertyGroupXML.Add(new XElement("IsAotCompatible", "true"));

        if (PublishAOT)
            propertyGroupXML.Add(new XElement("PublishAot", "true"));

        propertyGroupXML.Add(new XElement("DefaultItemExcludes", "**\\**"));
        propertyGroupXML.Add(new XElement("ProjectPath", scriptRoot.FullName));

        if (OutputPath != null)
            propertyGroupXML.Add(new XElement("OutputPath", OutputPath.FullName));

        if (IntermediateOutputPath != null)
            propertyGroupXML.Add(new XElement("BaseIntermediateOutputPath", IntermediateOutputPath.FullName));

        XElement scriptsXML = FindOrCreate(projectXML,
            new XElement("ItemGroup",
                new XAttribute("Label", "Compile")
            )
        );

        scriptsXML.RemoveNodes();

        scriptsXML.Add(
            scriptPaths.Select(x =>
                new XElement("Compile", new XAttribute("Include", $"$(ProjectPath){Path.DirectorySeparatorChar}{Path.GetRelativePath(scriptRoot.FullName, x.FullName)}"))
            )
        );

        XElement referencesXML = FindOrCreate(projectXML,
            new XElement("ItemGroup",
                new XAttribute("Label", "References")
            )
        );

        referencesXML.RemoveNodes();

        bool isPrivate = ReferencesArePrivate;

        referencesXML.Add(
            AssemblyReferences.Select(x => new XElement("Reference",
                new XAttribute("Include", x.Key),
                new XElement("HintPath", x.Value),
                new XElement("Private", isPrivate)
            ))
        );

        projectDocument.Save(projectFile.FullName);
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
