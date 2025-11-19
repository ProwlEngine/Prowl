# Prowl.Aspect - PostSharp Alternative for C#

A lightweight, open-source Aspect-Oriented Programming (AOP) framework for C#, inspired by PostSharp. Prowl.Aspect uses IL weaving with Mono.Cecil to inject aspect behavior at compile-time.

## 🎯 Project Status

**Current Status:** Test-Driven API Design Complete + Basic Weaver Infrastructure

- ✅ **77 comprehensive tests** defining the complete API
- ✅ Core aspect attribute classes (`OnMethodBoundaryAspect`, `LocationInterceptionAspect`)
- ✅ Method and property interception support
- ✅ FlowBehavior for controlling execution flow
- ✅ IL Weaver infrastructure with Mono.Cecil
- ⚠️ **Weaving implementation in progress** (basic structure complete, full IL generation TODO)

## 📦 Project Structure

```
Prowl.Aspect/
├── Aspect/                          # Core library with aspect attributes
│   ├── OnMethodBoundaryAspect.cs   # Base class for method interception
│   ├── LocationInterceptionAspect.cs # Base class for property interception
│   ├── MethodExecutionArgs.cs       # Context for method interception
│   ├── LocationInterceptionArgs.cs  # Context for property interception
│   ├── FlowBehavior.cs             # Enum for controlling execution flow
│   └── Arguments.cs                 # Wrapper for method arguments
│
├── Aspect.Weaver/                   # IL weaving engine using Mono.Cecil
│   ├── ModuleWeaver.cs             # Main orchestrator
│   ├── MethodBoundaryAspectWeaver.cs # Weaves method aspects
│   └── LocationInterceptionAspectWeaver.cs # Weaves property aspects
│
├── Aspect.Weaver.Host/              # Console app to run the weaver
│   └── Program.cs                   # CLI entry point
│
└── Aspect.Tests/                    # 77 comprehensive tests
    ├── MethodInterceptionTests.cs   # Method lifecycle tests
    ├── PropertyInterceptionTests.cs # Property interception tests
    ├── FlowBehaviourTests.cs       # Flow control tests
    ├── AttributeInheritanceTests.cs # Inheritance & multicast tests
    ├── PracticalAspectsTests.cs    # Real-world examples
    └── TestAspects.cs              # Shared test aspect implementations
```

## 🚀 Quick Start (Planned Usage)

### 1. Install the Package
```bash
dotnet add package Prowl.Aspect
```

### 2. Create an Aspect

```csharp
using Aspect;

[AttributeUsage(AttributeTargets.Method)]
public class LoggingAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        Console.WriteLine($"Entering {args.Method.Name}");
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        Console.WriteLine($"Exiting {args.Method.Name}");
    }
}
```

### 3. Apply the Aspect

```csharp
public class MyService
{
    [Logging]
    public void DoWork()
    {
        Console.WriteLine("Working...");
    }
}
```

### 4. Weave the Assembly

```bash
dotnet build
Aspect.Weaver.Host.exe bin/Debug/net10.0/MyApp.dll
```

## 📚 Core Features

### Method Interception (`OnMethodBoundaryAspect`)

Intercept method execution with lifecycle hooks:

```csharp
public class MyAspect : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        // Called before method executes
        // Can modify arguments, skip method, or throw
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        // Called after successful execution
        // Can modify return value
    }

    public override void OnException(MethodExecutionArgs args)
    {
        // Called when exception occurs
        // Can suppress, replace, or rethrow exception
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        // Always called (like finally)
    }
}
```

### Property Interception (`LocationInterceptionAspect`)

Intercept property getters and setters:

```csharp
public class NotifyPropertyChangedAttribute : LocationInterceptionAspect
{
    public override void OnSetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue(); // Get old value
        var oldValue = args.Value;

        args.ProceedSetValue(); // Set new value

        if (!Equals(oldValue, args.Value))
        {
            // Raise PropertyChanged event
            RaisePropertyChanged(args.Instance, args.Property.Name);
        }
    }

    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue(); // Get the actual value
        // Can modify args.Value before returning
    }
}
```

### Flow Behavior Control

Control execution flow from aspects:

```csharp
public class CacheAttribute : OnMethodBoundaryAspect
{
    private static Dictionary<string, object> _cache = new();

    public override void OnEntry(MethodExecutionArgs args)
    {
        var key = GenerateCacheKey(args);

        if (_cache.TryGetValue(key, out var cachedValue))
        {
            args.ReturnValue = cachedValue;
            args.FlowBehavior = FlowBehavior.Return; // Skip method execution
        }
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        var key = GenerateCacheKey(args);
        _cache[key] = args.ReturnValue;
    }
}
```

FlowBehavior options:
- `Continue` - Normal execution (default)
- `Return` - Skip method execution or suppress exception
- `ThrowException` - Throw custom exception

### Argument & Return Value Modification

```csharp
public class ArgumentValidationAttribute : OnMethodBoundaryAspect
{
    public override void OnEntry(MethodExecutionArgs args)
    {
        // Modify arguments
        if (args.Arguments[0] is int value && value < 0)
        {
            args.Arguments[0] = 0; // Clamp to zero
        }
    }
}

public class TransformResultAttribute : OnMethodBoundaryAspect
{
    public override void OnSuccess(MethodExecutionArgs args)
    {
        // Modify return value
        if (args.ReturnValue is string str)
        {
            args.ReturnValue = str.ToUpper();
        }
    }
}
```

## 🧪 Test Coverage

77 tests covering:

### Method Interception (17 tests)
- OnEntry/OnSuccess/OnException/OnExit lifecycle
- Argument modification
- Return value modification
- Exception handling
- Generic methods
- Void methods

### Property Interception (12 tests)
- OnGetValue/OnSetValue hooks
- Value modification (get and set)
- Skipping backing field access
- Auto-properties, read-only, write-only
- Change tracking

### Flow Behavior (12 tests)
- Continue/Return/ThrowException
- Method skipping
- Exception suppression and replacement
- Conditional execution
- Parameter validation

### Attribute Inheritance (12 tests)
- Class-level aspects
- Aspect inheritance to derived classes
- Multiple aspects with priority
- Interface and abstract method interception
- Multicast filtering

### Practical Examples (24 tests)
- **Caching** - Result caching with argument-based keys
- **Logging** - Entry/exit with timing and exceptions
- **NotifyPropertyChanged** - Automatic INotifyPropertyChanged
- **Retry** - Automatic retry on failure
- **Transaction** - Commit/rollback pattern
- **Authorization** - Role-based access control
- **Validation** - Parameter validation
- **Performance Monitoring** - Execution time tracking
- **Exception Handling** - Global exception handling

## 🔧 Implementation Status

### ✅ Completed
- Full API design with comprehensive tests
- Core aspect attribute classes
- Test infrastructure with 77 tests
- IL weaver project structure
- Mono.Cecil integration
- Aspect detection logic
- Console host application

### 🚧 In Progress
- IL generation for method interception
- FlowBehavior implementation
- Property getter/setter weaving
- MethodExecutionArgs population
- LocationInterceptionArgs implementation

### 📋 TODO
- Complete IL weaving for OnEntry/OnSuccess/OnException/OnExit
- Implement try-catch-finally wrapping
- Handle FlowBehavior.Return to skip methods
- Handle FlowBehavior.ThrowException
- Implement property backing field detection
- Weave ProceedGetValue/ProceedSetValue logic
- MSBuild integration for automatic weaving
- NuGet package creation
- Performance benchmarks
- More real-world examples

## 🎯 Design Goals

1. **PostSharp-like API** - Familiar syntax for developers coming from PostSharp
2. **Compile-time weaving** - No runtime overhead
3. **Test-driven** - Comprehensive test suite defining expected behavior
4. **Open source** - MIT license
5. **Mono.Cecil based** - Industry-standard IL weaving
6. **No async (initially)** - Simpler implementation, can add later

## 🤝 Contributing

This is currently a TDD project with all 77 tests written but IL weaving implementation in progress.

Great areas to contribute:
1. **IL Generation** - Complete the weaving logic in `MethodBoundaryAspectWeaver`
2. **Property Weaving** - Implement `LocationInterceptionAspectWeaver`
3. **FlowBehavior** - Handle all flow control scenarios
4. **MSBuild Integration** - Automatic weaving during build
5. **Documentation** - Examples and tutorials

## 📄 License

MIT License - see LICENSE file for details

## 🙏 Acknowledgments

- Inspired by [PostSharp](https://www.postsharp.net/)
- Built with [Mono.Cecil](https://github.com/jbevain/cecil)
- Test framework: [xUnit](https://xunit.net/)

---

**Note:** This project is in active development. The API is defined through tests but the IL weaving implementation is not yet complete. Contributions welcome!
