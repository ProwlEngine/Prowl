using Aspect;

Console.WriteLine("=== Aspect Weaving Example ===\n");

var calculator = new Calculator();

Console.WriteLine("Calling Add(5, 3):");
var result = calculator.Add(5, 3);
Console.WriteLine($"Result: {result}\n");

Console.WriteLine("Calling Divide(10, 2):");
var divResult = calculator.Divide(10, 2);
Console.WriteLine($"Result: {divResult}\n");

Console.WriteLine("Calling Divide(10, 0) - will throw:");
try
{
    calculator.Divide(10, 0);
}
catch (Exception ex)
{
    Console.WriteLine($"Caught: {ex.Message}\n");
}

Console.WriteLine("Done!");

// Simple calculator class with logging aspect
public class Calculator
{
    [LoggingAspect]
    public int Add(int a, int b)
    {
        Console.WriteLine($"  [Inside Add method: {a} + {b}]");
        return a + b;
    }

    [LoggingAspect]
    public int Divide(int a, int b)
    {
        Console.WriteLine($"  [Inside Divide method: {a} / {b}]");
        return a / b;
    }
}

// Simple logging aspect
[AttributeUsage(AttributeTargets.Method)]
public class LoggingAspectAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        Console.WriteLine($"[OnEntry] {args.Method.Name}");
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        Console.WriteLine($"[OnSuccess] {args.Method.Name} returned: {args.ReturnValue}");
    }

    public override void OnException(MethodExecutionArgs args)
    {
        Console.WriteLine($"[OnException] {args.Method.Name} threw: {args.Exception?.Message}");
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        Console.WriteLine($"[OnExit] {args.Method.Name}");
    }
}
