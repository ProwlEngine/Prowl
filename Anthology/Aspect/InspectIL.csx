#r "nuget: Mono.Cecil, 0.11.5"

using Mono.Cecil;
using Mono.Cecil.Cil;

var assemblyPath = Args[0];
var module = ModuleDefinition.ReadModule(assemblyPath);

var personType = module.Types.FirstOrDefault(t => t.Name == "Person");
if (personType == null)
{
    Console.WriteLine("Person type not found");
    return;
}

Console.WriteLine("=== Person.Name setter helper ===\n");
var nameSetterHelper = personType.Methods.FirstOrDefault(m => m.Name.Contains("<Name>__SetValueHelper"));
if (nameSetterHelper != null)
{
    Console.WriteLine($"Method: {nameSetterHelper.FullName}");
    Console.WriteLine($"IsStatic: {nameSetterHelper.IsStatic}");
    Console.WriteLine($"Parameters: {nameSetterHelper.Parameters.Count}");
    Console.WriteLine($"Variables: {nameSetterHelper.Body.Variables.Count}");
    Console.WriteLine("\nInstructions:");
    for (int i = 0; i < nameSetterHelper.Body.Instructions.Count; i++)
    {
        var instr = nameSetterHelper.Body.Instructions[i];
        Console.WriteLine($"  IL_{i:X4}: {instr.OpCode} {instr.Operand}");
    }
}
else
{
    Console.WriteLine("Setter helper not found");
}
