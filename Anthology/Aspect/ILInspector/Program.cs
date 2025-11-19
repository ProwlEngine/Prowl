using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length == 0)
{
    Console.WriteLine("Usage: ILInspector <assembly-path>");
    return;
}

var assemblyPath = args[0];
var module = ModuleDefinition.ReadModule(assemblyPath);

// Check Product type for field transformation
var productType = module.Types.FirstOrDefault(t => t.Name == "Product");
if (productType != null)
{
    Console.WriteLine("=== Product Type ===\n");
    Console.WriteLine($"Fields: {productType.Fields.Count}");
    foreach (var field in productType.Fields)
    {
        Console.WriteLine($"  Field: {field.Name} ({field.FieldType.Name}) - {field.Attributes}");
        Console.WriteLine($"    Attributes: {field.CustomAttributes.Count}");
        foreach (var attr in field.CustomAttributes)
        {
            Console.WriteLine($"      - {attr.AttributeType.FullName}");
        }
    }
    Console.WriteLine($"\nProperties: {productType.Properties.Count}");
    foreach (var prop in productType.Properties)
    {
        Console.WriteLine($"  Property: {prop.Name} ({prop.PropertyType.Name})");
        Console.WriteLine($"    Has getter: {prop.GetMethod != null}");
        Console.WriteLine($"    Has setter: {prop.SetMethod != null}");
    }
    Console.WriteLine();
}

var personType = module.Types.FirstOrDefault(t => t.Name == "Person");
if (personType == null)
{
    Console.WriteLine("Person type not found");
    return;
}

Console.WriteLine("=== Person.Name setter (original) ===\n");
var nameSetter = personType.Methods.FirstOrDefault(m => m.Name == "set_Name");
if (nameSetter != null)
{
    Console.WriteLine($"Method: {nameSetter.FullName}");
    Console.WriteLine($"Variables: {nameSetter.Body.Variables.Count}");
    for (int i = 0; i < nameSetter.Body.Variables.Count; i++)
    {
        Console.WriteLine($"  Var {i}: {nameSetter.Body.Variables[i].VariableType}");
    }
    Console.WriteLine("\nInstructions:");
    for (int i = 0; i < Math.Min(20, nameSetter.Body.Instructions.Count); i++)
    {
        var instr = nameSetter.Body.Instructions[i];
        Console.WriteLine($"  IL_{i:X4}: {instr.OpCode,-12} {instr.Operand}");
    }
}

Console.WriteLine("\n\n=== Person.Name setter helper ===\n");
var nameSetterHelper = personType.Methods.FirstOrDefault(m => m.Name.Contains("<Name>__SetValueHelper"));
if (nameSetterHelper != null)
{
    Console.WriteLine($"Method: {nameSetterHelper.FullName}");
    Console.WriteLine($"IsStatic: {nameSetterHelper.IsStatic}");
    Console.WriteLine($"Parameters: {nameSetterHelper.Parameters.Count}");
    Console.WriteLine($"Variables: {nameSetterHelper.Body.Variables.Count}");
    for (int i = 0; i < nameSetterHelper.Body.Variables.Count; i++)
    {
        Console.WriteLine($"  Var {i}: {nameSetterHelper.Body.Variables[i].VariableType}");
    }
    Console.WriteLine("\nInstructions:");
    for (int i = 0; i < nameSetterHelper.Body.Instructions.Count; i++)
    {
        var instr = nameSetterHelper.Body.Instructions[i];
        Console.WriteLine($"  IL_{i:X4}: {instr.OpCode,-12} {instr.Operand}");
    }
}
else
{
    Console.WriteLine("Setter helper not found");
}
