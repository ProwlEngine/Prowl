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

        // Import aspect base types
        ImportAspectTypes();

        // Find and weave all methods with aspects
        var methodWeaver = new MethodBoundaryAspectWeaver(_module);
        var propertyWeaver = new LocationInterceptionAspectWeaver(_module);
        var methodInterceptionWeaver = new MethodInterceptionAspectWeaver(_module);

        foreach (var type in _module.Types.ToList())
        {
            WeaveType(type, methodWeaver, propertyWeaver, methodInterceptionWeaver);
        }

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

    private void WeaveType(TypeDefinition type, MethodBoundaryAspectWeaver methodWeaver, LocationInterceptionAspectWeaver propertyWeaver, MethodInterceptionAspectWeaver methodInterceptionWeaver)
    {
        // Skip compiler-generated types
        if (type.Name.Contains("<") || type.Name.Contains(">"))
            return;

        // NOTE: Field-to-property transformation is disabled because it requires
        // rewriting all field access instructions throughout the assembly to use
        // property getters/setters instead. Users should use properties directly.
        // propertyWeaver.TransformFieldsToProperties(type);

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
}
