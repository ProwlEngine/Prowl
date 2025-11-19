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

// Test property interception
Console.WriteLine("=== Property Interception Example ===\n");
var person = new Person();

Console.WriteLine("Setting Name = 'John':");
person.Name = "John";

Console.WriteLine("\nGetting Name:");
var name = person.Name;
Console.WriteLine($"Got value: {name}\n");

Console.WriteLine("Setting Age = 25:");
person.Age = 25;

Console.WriteLine("\nGetting Age:");
var age = person.Age;
Console.WriteLine($"Got value: {age}\n");

// Test field interception (fields should be transformed to properties)
Console.WriteLine("\n=== Field Interception Example ===\n");
var product = new Product();

Console.WriteLine("Setting Price = 99.99:");
product.Price = 99.99m;

Console.WriteLine("\nGetting Price:");
var price = product.Price;
Console.WriteLine($"Got value: {price}\n");

Console.WriteLine("Setting Stock = 10:");
product.Stock = 10;

Console.WriteLine("\nGetting Stock:");
var stock = product.Stock;
Console.WriteLine($"Got value: {stock}\n");

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

// Person class with property logging
public class Person
{
    [PropertyLogging]
    public string Name { get; set; }

    [PropertyLogging]
    public int Age { get; set; }
}

// Product class with field logging (fields should be auto-converted to properties)
public class Product
{
    [FieldLogging]
    public decimal Price;

    [FieldLogging]
    public int Stock;
}

// Property logging aspect
[AttributeUsage(AttributeTargets.Property)]
public class PropertyLoggingAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        Console.WriteLine($"  [OnGetValue] Property: {args.Property.Name}");
        args.ProceedGetValue();
        Console.WriteLine($"  [OnGetValue] Returning: {args.Value}");
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        Console.WriteLine($"  [OnSetValue] Property: {args.Property.Name}, Value: {args.Value}");
        args.ProceedSetValue();
        Console.WriteLine($"  [OnSetValue] Set complete");
    }
}

// Field logging aspect (fields will be converted to properties automatically)
[AttributeUsage(AttributeTargets.Field)]
public class FieldLoggingAttribute : LocationInterceptionAspect
{
    public override void OnGetValue(LocationInterceptionArgs args)
    {
        Console.WriteLine($"  [OnGetValue] Field->Property: {args.Property.Name}");
        args.ProceedGetValue();
        Console.WriteLine($"  [OnGetValue] Returning: {args.Value}");
    }

    public override void OnSetValue(LocationInterceptionArgs args)
    {
        Console.WriteLine($"  [OnSetValue] Field->Property: {args.Property.Name}, Value: {args.Value}");
        args.ProceedSetValue();
        Console.WriteLine($"  [OnSetValue] Set complete");
    }
}
