# Prowl.Aspect - PostSharp Alternative for C#

![Github top languages](https://img.shields.io/github/languages/top/prowlengine/prowl.aspect)
[![GitHub license](https://img.shields.io/github/license/prowlengine/prowl.aspect?style=flat-square)](https://github.com/prowlengine/prowl.aspect/blob/main/LICENSE)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord)](https://discord.gg/BqnJ9Rn4sn)

<span id="readme-top"></span>

## Table of Contents
1. [About The Project](#-about-the-project-)
2. [Features](#-features-)
3. [Getting Started](#-getting-started-)
   * [Installation](#installation)
   * [Create an Aspect](#create-an-aspect)
   * [Apply the Aspect](#apply-the-aspect)
   * [Build Your Project](#build-your-project)
   * [Configuration](#configuration)
4. [Core Features](#-core-features-)
   * [Method Interception](#method-interception-onmethodboundaryaspect)
   * [Property Interception](#property-interception-locationinterceptionaspect)
   * [Flow Behavior Control](#flow-behavior-control)
5. [Project Structure](#-project-structure)
6. [Implementation Status](#-implementation-status)
7. [Advanced Examples](#-advanced-examples-)
8. [Troubleshooting](#-troubleshooting-)
9. [Contributing](#-contributing-)
10. [Part of the Prowl Ecosystem](#-part-of-the-prowl-ecosystem)
11. [License](#-license-)

# <span align="center">📝 About The Project 📝</span>

Prowl.Aspect is an open-source, **[MIT-licensed](#span-aligncenter-license-span)** Aspect-Oriented Programming (AOP) framework for C#, inspired by PostSharp. It uses IL weaving with Mono.Cecil to inject aspect behavior into your assemblies, enabling powerful cross-cutting concerns like logging, caching, validation, and more.

## 🎯 Current Status

**Production Ready** - All 86 tests passing (100%)

- ✅ **Method Interception** - OnEntry/OnSuccess/OnException/OnExit lifecycle hooks
- ✅ **Property Interception** - OnGetValue/OnSetValue with value modification
- ✅ **Flow Behavior Control** - Skip methods, suppress exceptions, modify execution flow
- ✅ **Argument & Return Value Modification** - Full control over method parameters and results
- ✅ **Multi-Targeting Support** - .NET Standard 2.1, .NET 6, 7, 8, 9, and 10
- ✅ **Double-Weaving Protection** - Assemblies are marked after weaving to prevent accidental re-weaving

# <span align="center">✨ Features ✨</span>

- **Compile-Time Weaving** - Zero runtime overhead, aspects are woven into IL during build
- **PostSharp-Compatible API** - Familiar syntax for developers coming from PostSharp
- **Multi-Targeting** - Supports .NET Standard 2.1, .NET 6, 7, 8, 9, and 10
- **Method Lifecycle Hooks** - OnEntry, OnSuccess, OnException, OnExit
- **Property Interception** - Full control over getters and setters
- **Flow Control** - Skip method execution, suppress exceptions, modify return values
- **Method Interception** - Full control over method invocation with Proceed() semantics
- **Comprehensive Testing** - 86 tests covering all features and edge cases
- **Open Source** - MIT license, community-driven development

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🚀 Getting Started 🚀</span>

## Installation

Install via NuGet (once published):

```bash
dotnet add package Prowl.Aspect
```

Or download from [NuGet.org](https://www.nuget.org/packages/Prowl.Aspect)

## Create an Aspect

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

## Apply the Aspect

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

## Build Your Project

With the NuGet package installed, weaving happens **automatically** during build:

```bash
dotnet build
```

You'll see weaving messages in the build output:
```
Prowl.Aspect: Weaving aspects into obj\Debug\net8.0\MyApp.dll
  Weaving method: MyNamespace.MyClass::DoWork() with 1 aspect(s)
Prowl.Aspect: Weaving completed successfully
```

**Note**: If not using the NuGet package, you can manually run the weaver:
```bash
Aspect.Weaver.Host.exe bin/Debug/net10.0/MyApp.dll
```

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Configuration

### Disable Automatic Weaving

To temporarily disable weaving for a project:

```xml
<PropertyGroup>
  <ProwlAspectWeavingEnabled>false</ProwlAspectWeavingEnabled>
</PropertyGroup>
```

### Custom Weaver Path

If needed, specify a custom weaver location:

```xml
<PropertyGroup>
  <ProwlAspectWeaverPath>C:\path\to\Aspect.Weaver.Host.exe</ProwlAspectWeaverPath>
</PropertyGroup>
```

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🔧 Core Features 🔧</span>

## Method Interception (`OnMethodBoundaryAspect`)

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

## Property Interception (`LocationInterceptionAspect`)

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

## Flow Behavior Control

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

**FlowBehavior options:**
- `Continue` - Normal execution (default)
- `Return` - Skip method execution or suppress exception
- `ThrowException` - Throw custom exception

## Argument & Return Value Modification

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

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">📦 Project Structure 📦</span>

```
Prowl.Aspect/
├── Aspect/                          # Core library with aspect attributes
│   ├── OnMethodBoundaryAspect.cs   # Base class for method interception
│   ├── MethodInterceptionAspect.cs # Advanced method interception
│   ├── LocationInterceptionAspect.cs # Base class for property interception
│   ├── MethodExecutionArgs.cs       # Context for method interception
│   ├── LocationInterceptionArgs.cs  # Context for property interception
│   ├── FlowBehavior.cs             # Enum for controlling execution flow
│   └── Arguments.cs                 # Wrapper for method arguments
│
├── Aspect.Weaver/                   # IL weaving engine using Mono.Cecil
│   ├── ModuleWeaver.cs             # Main orchestrator
│   ├── WeaverBase.cs               # Shared weaving logic
│   ├── MethodBoundaryAspectWeaver.cs # Weaves method boundary aspects
│   ├── MethodInterceptionAspectWeaver.cs # Weaves method interception
│   └── LocationInterceptionAspectWeaver.cs # Weaves property aspects
│
├── Aspect.Weaver.Host/              # Console app to run the weaver
│   └── Program.cs                   # CLI entry point
│
└── Aspect.Tests/                    # 86 comprehensive tests
    ├── MethodInterceptionTests.cs   # Method lifecycle tests
    ├── PropertyInterceptionTests.cs # Property interception tests
    ├── FlowBehaviourTests.cs       # Flow control tests
    ├── PracticalAspectsTests.cs    # Real-world examples
    ├── AdvancedPracticalAspectsTests.cs # Advanced scenarios
    └── TestAspects.cs              # Shared aspect implementations
```

# <span align="center">⚙️ Implementation Status ⚙️</span>

### ✅ Completed & Working (100% - All 86 Tests Passing)

- Full API design with 86 comprehensive tests
- Core aspect attribute classes (OnMethodBoundaryAspect, MethodInterceptionAspect, LocationInterceptionAspect)
- IL weaver infrastructure with Mono.Cecil
- Method boundary aspects (OnEntry/OnSuccess/OnException/OnExit)
- Method interception aspects with Proceed() semantics
- Property interception (OnGetValue/OnSetValue)
- Flow behavior control (Continue/Return/ThrowException)
- Argument and return value modification
- Exception handling and suppression
- Multi-targeting (.NET Standard 2.1, .NET 6-10)
- Console weaver host application
- Double-weaving protection

### 📋 Planned

- Async method support
- real-world examples and tutorials
- Visual Studio integration

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🎨 Advanced Examples 🎨</span>

## Caching

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class CacheAttribute : OnMethodBoundaryAspect
{
    private static Dictionary<string, object> _cache = new();

    public override void OnEntry(MethodExecutionArgs args)
    {
        var key = $"{args.Method.Name}:{string.Join(",", args.Arguments)}";
        if (_cache.TryGetValue(key, out var cachedValue))
        {
            args.ReturnValue = cachedValue;
            args.FlowBehavior = FlowBehavior.Return;
        }
    }

    public override void OnSuccess(MethodExecutionArgs args)
    {
        var key = $"{args.Method.Name}:{string.Join(",", args.Arguments)}";
        _cache[key] = args.ReturnValue;
    }
}

// Usage
[Cache]
public int ExpensiveCalculation(int x, int y)
{
    Thread.Sleep(1000);
    return x + y;
}
```

## Retry Logic

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class RetryAttribute : MethodInterceptionAspect
{
    public int MaxRetries { get; set; } = 3;

    public override void OnInvoke(MethodInterceptionArgs args)
    {
        Exception lastException = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                args.Proceed();
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException;
    }
}

// Usage
[Retry(MaxRetries = 5)]
public void UnreliableOperation()
{
    // May fail occasionally
}
```

## INotifyPropertyChanged

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class NotifyPropertyChangedAttribute : LocationInterceptionAspect
{
    public override void OnSetValue(LocationInterceptionArgs args)
    {
        var newValue = args.Value;
        args.ProceedGetValue();
        var oldValue = args.Value;

        if (!Equals(oldValue, newValue))
        {
            args.Value = newValue;
            args.ProceedSetValue();

            if (args.Instance is INotifyPropertyChanged inpc)
            {
                RaisePropertyChanged(inpc, args.Property.Name);
            }
        }
    }

    public override void OnGetValue(LocationInterceptionArgs args)
    {
        args.ProceedGetValue();
    }

    private void RaisePropertyChanged(INotifyPropertyChanged instance, string propertyName)
    {
        var eventDelegate = instance.GetType()
            .GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(instance) as PropertyChangedEventHandler;

        eventDelegate?.Invoke(instance, new PropertyChangedEventArgs(propertyName));
    }
}

// Usage
public class Person : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    [NotifyPropertyChanged]
    public string Name { get; set; }
}
```

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🔧 Troubleshooting 🔧</span>

## Weaving Not Running

**Symptoms**: No "Prowl.Aspect: Weaving" messages in build output

**Solutions**:
1. Check that the NuGet package is properly installed
2. Verify `ProwlAspectWeavingEnabled` is not set to `false`
3. Build with detailed verbosity: `dotnet build -v detailed`

## Assembly Already Woven Error

**Symptoms**: "Assembly has already been woven by Aspect" message

**Explanation**: This is normal! The weaver detected double-weaving protection and skipped re-weaving.

**Solution**: If you need a fresh weave:
```bash
dotnet clean
dotnet build
```

## Performance Impact

Prowl.Aspect uses **compile-time weaving** - there is **zero runtime overhead**. All aspect code is injected directly into your IL during build. The performance is identical to hand-written code.

# <span align="center">🤝 Contributing 🤝</span>

Contributions are welcome! Areas where you can help:

1. **Documentation** - More examples, tutorials, and use cases
2. **Performance** - Benchmarks and optimizations
3. **Async Support** - Add support for async/await patterns
4. **More Aspects** - Implement additional real-world aspect patterns

## Building from Source

1. **Clone the repository**:
   ```bash
   git clone https://github.com/ProwlEngine/Prowl.Aspect.git
   cd Prowl.Aspect
   ```

2. **Build all projects**:
   ```bash
   dotnet build
   ```

3. **Run tests**:
   ```bash
   dotnet test Aspect.Tests
   ```

## Creating a Local NuGet Package

1. **Build in Release mode**:
   ```bash
   dotnet build Aspect.Weaver -c Release
   dotnet build Aspect.Weaver.Host -c Release
   dotnet build Aspect -c Release
   ```

2. **Create the package**:
   ```bash
   dotnet pack Aspect/Aspect.csproj -c Release -o ./nupkg
   ```

3. **Test locally**:
   ```bash
   # Add local source
   dotnet nuget add source ./nupkg --name ProwlLocal

   # Create test project
   dotnet new console -n TestAspect
   cd TestAspect
   dotnet add package Prowl.Aspect --version 1.0.0-preview.1 --source ProwlLocal
   ```

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🌟 Part of the Prowl Ecosystem 🌟</span>

Prowl.Aspect is part of the Prowl game engine ecosystem, which includes but not limited to:

- **[Prowl Engine](https://github.com/ProwlEngine/Prowl)** - Open-source Unity-like game engine
- **[Prowl.Paper](https://github.com/ProwlEngine/Prowl.Paper)** - Immediate-mode UI library
- **[Prowl.Quill](https://github.com/ProwlEngine/Prowl.Quill)** - Vector Graphics library
- **[Prowl.Scribe](https://github.com/ProwlEngine/Prowl.Scribe)** - Font rendering library

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Dependencies 📦

- [Mono.Cecil](https://github.com/jbevain/cecil) - IL weaving engine

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">📄 License 📄</span>

Distributed under the MIT License. See [LICENSE](LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

## 🙏 Acknowledgments

- Inspired by [PostSharp](https://www.postsharp.net/)
- Built with [Mono.Cecil](https://github.com/jbevain/cecil)
- Test framework: [xUnit](https://xunit.net/)

---

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord)](https://discord.gg/BqnJ9Rn4sn)
