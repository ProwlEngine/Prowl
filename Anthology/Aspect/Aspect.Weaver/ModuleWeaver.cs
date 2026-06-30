using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace Aspect.Weaver;

/// <summary>
/// Main weaver class that orchestrates IL weaving for aspects.
/// </summary>
public class ModuleWeaver
{
    private ModuleDefinition _module = null!;
    private TypeReference _aspectAttributeTypeRef = null!;
    private TypeReference _onMethodBoundaryAspectTypeRef = null!;
    private TypeReference _locationInterceptionAspectTypeRef = null!;

    public void Weave(string assemblyPath)
    {
        Console.WriteLine($"Weaving assembly: {assemblyPath}");

        // Load the assembly
        var readerParameters = new ReaderParameters { ReadSymbols = true, ReadWrite = true };
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
        _module = assembly.MainModule;

        // Check if already woven to prevent double-weaving
        if (IsAlreadyWoven(assembly))
        {
            Console.WriteLine("Assembly has already been woven by Aspect. Skipping to prevent double-weaving.");
            return;
        }

        // Import aspect base types
        ImportAspectTypes();

        // Find and weave all methods with aspects
        var methodWeaver = new MethodBoundaryAspectWeaver(_module);
        var propertyWeaver = new LocationInterceptionAspectWeaver(_module);
        var methodInterceptionWeaver = new MethodInterceptionAspectWeaver(_module);
        var fieldAccessReplacer = new FieldAccessReplacer(_module);

        // First pass: transform fields to properties and collect replacements
        foreach (var type in _module.Types.ToList())
        {
            TransformFieldsInType(type, propertyWeaver, fieldAccessReplacer);
        }

        // Second pass: replace all field access instructions with property calls
        fieldAccessReplacer.ProcessModule();

        // Third pass: weave aspects into methods and properties
        foreach (var type in _module.Types.ToList())
        {
            WeaveType(type, methodWeaver, propertyWeaver, methodInterceptionWeaver);
        }

        // Mark assembly as woven before saving
        MarkAsWoven(assembly);

        // Save the modified assembly
        try
        {
            Console.WriteLine($"Writing woven assembly back to: {assemblyPath}");
            var writerParameters = new WriterParameters { WriteSymbols = true };
            assembly.Write(writerParameters);
            Console.WriteLine("Assembly written successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR writing assembly: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }

        Console.WriteLine("Weaving completed successfully!");
    }

    private void TransformFieldsInType(TypeDefinition type, LocationInterceptionAspectWeaver propertyWeaver, FieldAccessReplacer fieldAccessReplacer)
    {
        // Skip compiler-generated types
        if (type.Name.Contains("<") || type.Name.Contains(">"))
            return;

        // Transform fields with LocationInterceptionAspect to properties
        propertyWeaver.TransformFieldsToProperties(type, fieldAccessReplacer);

        // Process nested types recursively
        foreach (var nestedType in type.NestedTypes.ToList())
        {
            TransformFieldsInType(nestedType, propertyWeaver, fieldAccessReplacer);
        }
    }

    private void WeaveType(TypeDefinition type, MethodBoundaryAspectWeaver methodWeaver, LocationInterceptionAspectWeaver propertyWeaver, MethodInterceptionAspectWeaver methodInterceptionWeaver)
    {
        // Skip compiler-generated types
        if (type.Name.Contains("<") || type.Name.Contains(">"))
            return;

        // Weave methods
        foreach (var method in type.Methods.ToList())
        {
            if (method.IsConstructor || method.IsGetter || method.IsSetter)
                continue;

            // Check for method interception aspects first
            methodInterceptionWeaver.WeaveMethod(method);
            // Then check for boundary aspects
            methodWeaver.WeaveMethod(method);
        }

        // Weave properties
        foreach (var property in type.Properties.ToList())
        {
            propertyWeaver.WeaveProperty(property);
        }

        // Process nested types
        foreach (var nestedType in type.NestedTypes.ToList())
        {
            WeaveType(nestedType, methodWeaver, propertyWeaver, methodInterceptionWeaver);
        }
    }

    private void ImportAspectTypes()
    {
        // These will be imported from the Aspect assembly at runtime
        // For now, we'll resolve them dynamically during weaving
    }

    /// <summary>
    /// Checks if the assembly has already been woven by looking for the WeavedByAspect attribute.
    /// </summary>
    private bool IsAlreadyWoven(AssemblyDefinition assembly)
    {
        var weavedAttr = assembly.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "WeavedByAspectAttribute");

        if (weavedAttr != null)
        {
            var version = weavedAttr.ConstructorArguments.Count > 0
                ? weavedAttr.ConstructorArguments[0].Value as string
                : "unknown";
            Console.WriteLine($"Assembly was already woven by Aspect version: {version}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Marks the assembly as woven by adding the WeavedByAspect attribute.
    /// </summary>
    private void MarkAsWoven(AssemblyDefinition assembly)
    {
        // Find the WeavedByAspectAttribute type - first try in current module, then in referenced assemblies
        TypeDefinition? attrType = _module.Types
            .FirstOrDefault(t => t.FullName == "Aspect.WeavedByAspectAttribute");

        if (attrType == null)
        {
            // Look in referenced assemblies
            foreach (var assemblyRef in _module.AssemblyReferences)
            {
                if (assemblyRef.Name == "Aspect")
                {
                    try
                    {
                        var aspectAssembly = _module.AssemblyResolver.Resolve(assemblyRef);
                        attrType = aspectAssembly.MainModule.Types
                            .FirstOrDefault(t => t.FullName == "Aspect.WeavedByAspectAttribute");
                        if (attrType != null)
                            break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARNING: Could not resolve Aspect assembly: {ex.Message}");
                    }
                }
            }
        }

        if (attrType == null)
        {
            Console.WriteLine("WARNING: Could not find WeavedByAspectAttribute type to mark assembly as woven.");
            return;
        }

        // Find the constructor that takes a string parameter
        var ctor = attrType.Methods
            .FirstOrDefault(m => m.IsConstructor &&
                               m.Parameters.Count == 1 &&
                               m.Parameters[0].ParameterType.FullName == "System.String");

        if (ctor == null)
        {
            Console.WriteLine("WARNING: Could not find WeavedByAspectAttribute constructor.");
            return;
        }

        // Create and add the attribute
        var attr = new CustomAttribute(_module.ImportReference(ctor));
        attr.ConstructorArguments.Add(new CustomAttributeArgument(
            _module.TypeSystem.String,
            "1.0.0")); // Version number - could be read from assembly version in future

        assembly.CustomAttributes.Add(attr);
        Console.WriteLine("Marked assembly as woven by Aspect v1.0.0");
    }
}
