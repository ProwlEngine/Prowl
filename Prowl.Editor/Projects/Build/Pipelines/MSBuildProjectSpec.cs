// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;

namespace Prowl.Editor.Build;

public sealed record PackageRef(string Id, string Version);

/// <summary>A reference resolved from a path rather than a package, for engine assemblies.</summary>
public sealed record AssemblyRef(string Name, string HintPath);

public sealed record EmbeddedResourceRef(string Path, string LogicalName);

/// <summary>
/// The project file a build generates, as data.
/// </summary>
/// <remarks>
/// Generating a csproj is legitimate, because a csproj is a data file and it is what carries
/// <c>PublishAot</c>, <c>PublishTrimmed</c> and the runtime identifiers. Generating program source is
/// not, which is why the player is a compiled project instead.
/// <para>
/// A new platform describes its project by filling this in rather than assembling XML by hand, which is
/// the difference between adding a target and reimplementing the desktop pipeline.
/// </para>
/// </remarks>
public sealed record MSBuildProjectSpec
{
    public string Sdk { get; init; } = "Microsoft.NET.Sdk";

    public required string TargetFramework { get; init; }

    /// <summary>
    /// Plural, because several real targets are not one architecture: an Android bundle carries multiple
    /// ABIs and a macOS universal binary is merged from two. One emits the singular property, several
    /// emit the plural one, which is what the SDK expects.
    /// </summary>
    public IReadOnlyList<string> RuntimeIdentifiers { get; init; } = [];

    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<AssemblyRef> References { get; init; } = [];
    public IReadOnlyList<PackageRef> Packages { get; init; } = [];
    public IReadOnlyList<string> Compile { get; init; } = [];
    public IReadOnlyList<EmbeddedResourceRef> EmbeddedResources { get; init; } = [];
    public IReadOnlyList<string> TrimmerRootAssemblies { get; init; } = [];

    /// <summary>
    /// Renders the project file. Every group is sorted, so an unchanged build produces an identical file
    /// whatever order the caller collected things in, and every value is escaped.
    /// </summary>
    public string ToXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<Project Sdk=\"{Escape(Sdk)}\">");

        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{Escape(TargetFramework)}</TargetFramework>");

        if (RuntimeIdentifiers.Count == 1)
            sb.AppendLine($"    <RuntimeIdentifier>{Escape(RuntimeIdentifiers[0])}</RuntimeIdentifier>");
        else if (RuntimeIdentifiers.Count > 1)
            sb.AppendLine($"    <RuntimeIdentifiers>{Escape(string.Join(';', RuntimeIdentifiers))}</RuntimeIdentifiers>");

        foreach (var (key, value) in Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!IsValidElementName(key))
                throw new InvalidOperationException($"'{key}' is not a usable MSBuild property name.");

            sb.AppendLine($"    <{key}>{Escape(value)}</{key}>");
        }

        sb.AppendLine("  </PropertyGroup>");

        if (References.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var reference in References.OrderBy(r => r.Name, StringComparer.Ordinal))
            {
                sb.AppendLine($"    <Reference Include=\"{Escape(reference.Name)}\">");
                sb.AppendLine($"      <HintPath>{Escape(reference.HintPath)}</HintPath>");
                // Private forces a fresh copy from the hint path rather than whatever probing finds.
                sb.AppendLine("      <Private>true</Private>");
                sb.AppendLine("      <SpecificVersion>false</SpecificVersion>");
                sb.AppendLine("    </Reference>");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        if (Packages.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var package in Packages.OrderBy(p => p.Id, StringComparer.Ordinal))
                sb.AppendLine($"    <PackageReference Include=\"{Escape(package.Id)}\" Version=\"{Escape(package.Version)}\" />");
            sb.AppendLine("  </ItemGroup>");
        }

        if (TrimmerRootAssemblies.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (string assembly in TrimmerRootAssemblies.OrderBy(a => a, StringComparer.Ordinal))
                sb.AppendLine($"    <TrimmerRootAssembly Include=\"{Escape(assembly)}\" />");
            sb.AppendLine("  </ItemGroup>");
        }

        if (Compile.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (string file in Compile.OrderBy(f => f, StringComparer.Ordinal))
                sb.AppendLine($"    <Compile Include=\"{Escape(file)}\" />");
            sb.AppendLine("  </ItemGroup>");
        }

        if (EmbeddedResources.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var resource in EmbeddedResources.OrderBy(r => r.Path, StringComparer.Ordinal))
            {
                sb.AppendLine($"    <EmbeddedResource Include=\"{Escape(resource.Path)}\">");
                sb.AppendLine($"      <LogicalName>{Escape(resource.LogicalName)}</LogicalName>");
                sb.AppendLine("    </EmbeddedResource>");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    /// <summary>
    /// XML escaping plus MSBuild's own. MSBuild reads <c>%xx</c> as an escape and <c>$(...)</c> as a
    /// property reference, so a project under a folder named "100%Done" resolves to a path that does not
    /// exist unless the percent is escaped as its own code. Semicolons are left alone deliberately: they
    /// separate the values in <c>DefineConstants</c> and <c>RuntimeIdentifiers</c>.
    /// </summary>
    private static string Escape(string value)
    {
        string escaped = value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("$", "%24", StringComparison.Ordinal);

        return SecurityElement.Escape(escaped) ?? escaped;
    }

    // A property name reaches the XML as a tag, so it has to be a name XML accepts.
    private static bool IsValidElementName(string name)
        => name.Length > 0
            && (char.IsLetter(name[0]) || name[0] == '_')
            && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
}
