namespace Prowl.Wicked.CodeGen;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Prowl.Wicked.CodeGen.Processors;

/// <summary>
/// Main entry point for the Prowl.Wicked IL weaver.
/// Processes assemblies to transform [Command], [ClientRpc], [TargetRpc] methods and [SyncVar] fields.
/// </summary>
public class WickedWeaver
{
    private readonly ModuleDefinition _module;
    private readonly CommandProcessor _commandProcessor;
    private readonly ClientRpcProcessor _clientRpcProcessor;
    private readonly TargetRpcProcessor _targetRpcProcessor;
    private readonly SyncVarProcessor _syncVarProcessor;

    public WickedWeaver(ModuleDefinition module)
    {
        _module = module;
        _commandProcessor = new CommandProcessor(module);
        _clientRpcProcessor = new ClientRpcProcessor(module);
        _targetRpcProcessor = new TargetRpcProcessor(module);
        _syncVarProcessor = new SyncVarProcessor(module);
    }

    private const string WeavedMarkerAttribute = "Prowl.Wicked.WickedWeavedAttribute";

    /// <summary>
    /// Weaves an assembly file in place.
    /// </summary>
    public static bool Weave(string assemblyPath)
    {
        Console.WriteLine($"WickedWeaver: Processing {assemblyPath}");

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);

        var readerParams = new ReaderParameters
        {
            ReadWrite = true,
            ReadSymbols = File.Exists(Path.ChangeExtension(assemblyPath, ".pdb")),
            AssemblyResolver = resolver
        };

        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParams);

            // Check if already weaved
            if (IsAlreadyWeaved(assembly))
            {
                Console.WriteLine("WickedWeaver: Assembly already weaved, skipping");
                return false;
            }

            var weaver = new WickedWeaver(assembly.MainModule);

            if (weaver.Process())
            {
                // Mark as weaved to prevent double weaving
                MarkAsWeaved(assembly);

                var writerParams = new WriterParameters
                {
                    WriteSymbols = readerParams.ReadSymbols
                };
                assembly.Write(writerParams);
                Console.WriteLine("WickedWeaver: Weaving complete");
                return true;
            }
            else
            {
                Console.WriteLine("WickedWeaver: No changes needed");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WickedWeaver: Error - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if an assembly has already been weaved.
    /// </summary>
    private static bool IsAlreadyWeaved(AssemblyDefinition assembly)
    {
        return assembly.CustomAttributes.Any(a => a.AttributeType.FullName == WeavedMarkerAttribute);
    }

    /// <summary>
    /// Marks an assembly as weaved by adding a custom attribute.
    /// </summary>
    private static void MarkAsWeaved(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;

        // Create the marker attribute type if it doesn't exist
        var attrType = module.Types.FirstOrDefault(t => t.FullName == WeavedMarkerAttribute);
        if (attrType == null)
        {
            attrType = new TypeDefinition(
                "Prowl.Wicked",
                "WickedWeavedAttribute",
                TypeAttributes.NotPublic | TypeAttributes.Sealed,
                module.ImportReference(typeof(Attribute)));

            // Add default constructor
            var ctor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var il = ctor.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            // Attribute has a protected constructor, use NonPublic to find it
            var attrCtor = typeof(Attribute).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (attrCtor == null)
                throw new InvalidOperationException("Could not find Attribute constructor");
            il.Emit(OpCodes.Call, module.ImportReference(attrCtor));
            il.Emit(OpCodes.Ret);
            attrType.Methods.Add(ctor);

            module.Types.Add(attrType);
        }

        // Add the attribute to the assembly
        var ctorRef = attrType.Methods.First(m => m.IsConstructor);
        var attr = new CustomAttribute(ctorRef);
        assembly.CustomAttributes.Add(attr);
    }

    /// <summary>
    /// Processes all types in the module.
    /// Returns true if any changes were made.
    /// </summary>
    public bool Process()
    {
        bool modified = false;

        // First pass: Process SyncVars in all types
        foreach (var type in _module.Types)
        {
            if (ProcessSyncVars(type))
                modified = true;
        }

        // After all SyncVars are processed, rewrite field references across the entire module
        _syncVarProcessor.RewriteAllFieldReferences();

        // Second pass: Process RPCs
        foreach (var type in _module.Types)
        {
            if (ProcessRpcs(type))
                modified = true;
        }

        return modified;
    }

    private bool ProcessSyncVars(TypeDefinition type)
    {
        bool modified = false;

        // Process nested types first
        foreach (var nested in type.NestedTypes)
        {
            if (ProcessSyncVars(nested))
                modified = true;
        }

        // Process SyncVars in this type
        if (_syncVarProcessor.Process(type))
            modified = true;

        return modified;
    }

    private bool ProcessRpcs(TypeDefinition type)
    {
        bool modified = false;

        // Process nested types
        foreach (var nested in type.NestedTypes)
        {
            if (ProcessRpcs(nested))
                modified = true;
        }

        // Check if type inherits from EntityBehaviour
        if (!InheritsFromEntityBehaviour(type))
            return modified;

        // Process each method for RPC attributes
        foreach (var method in type.Methods.ToArray())
        {
            if (_commandProcessor.Process(method))
                modified = true;
            else if (_clientRpcProcessor.Process(method))
                modified = true;
            else if (_targetRpcProcessor.Process(method))
                modified = true;
        }

        return modified;
    }

    private bool InheritsFromEntityBehaviour(TypeDefinition type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.FullName == "Prowl.Wicked.Core.EntityBehaviour")
                return true;

            try
            {
                var resolved = current.Resolve();
                current = resolved?.BaseType;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }
}
