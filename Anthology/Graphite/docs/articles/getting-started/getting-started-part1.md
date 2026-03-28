---
uid: getting-started-part1
---

# Part 1

In this section, we will set up a new project, create a Window to draw into, and create a Veldrid GraphicsDevice. We will also set up a very simple skeleton for the other sections to fill in.

## Create a new Project

This walkthrough assumes you are using the .NET Core toolchain, although the same code could be run on .NET Framework or Mono. Veldrid is a .NET Standard 2.0 library, so you will a version of the SDK supporting .NET Core 2.0 or higher.

Create a new console application by running `dotnet new console`, or by using a "New Project" dialogue in Visual Studio or other IDE. Then, add a reference to these two NuGet packages:

* Veldrid: the core package containing all of the important graphics functionality.
* Veldrid.StartupUtilities: a utility package that makes it easy to set up an application using SDL2.
* Veldrid.SPIRV: a utility package that provides runtime shader compilation and translation.

You can add references to these packages using the Visual Studio package manager, or you can add the following lines directly into your .csproj.

```XML
<ItemGroup>
  <PackageReference Include="Veldrid" Version="4.8.0" />
  <PackageReference Include="Veldrid.StartupUtilities" Version="4.8.0" />
  <PackageReference Include="Veldrid.SPIRV" Version="1.0.13" />
</ItemGroup>
```

## Creating a Window

Veldrid itself does not care what framework or library you use to manage your window or render view -- it is flexible enough to work with many different systems. The `VeldridStartup` static class (from Veldrid.StartupUtilities) includes a number of helper functions intended to make Window and GraphicsDevice creation easier for common scenarios, and it will be used here.

First, we will create a Window. Inside the `Main()` method, let's add some code.

```C#
WindowCreateInfo windowCI = new WindowCreateInfo()
{
    X = 100,
    Y = 100,
    WindowWidth = 960,
    WindowHeight = 540,
    WindowTitle = "Veldrid Tutorial"
};
Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);
```

Behind the scenes, the StartupUtilities package is using [SDL2](https://www.libsdl.org/) to create and manage windows for us. The Sdl2Window class is a simple wrapper which lets you do common things like resize the window and respond to user input.

Next, we will create a GraphicsDevice attached to our window, which will let us issue graphics commands. Create a new [GraphicsDevice](xref:Veldrid.GraphicsDevice) field called `_graphicsDevice`. We want to create this device with a couple of common options enabled. We will use another `VeldridStartup` helper method to create it:

```C#
GraphicsDeviceOptions options = new GraphicsDeviceOptions
{
    PreferStandardClipSpaceYDirection = true,
    PreferDepthRangeZeroToOne = true
};
_graphicsDevice = VeldridStartup.CreateGraphicsDevice(window, options);
```

Next, let's add in a very basic application loop that keeps the window running. At the bottom of `Main()`:

```C#
while (window.Exists)
{
    window.PumpEvents();
}
```

If we run this code, we will not see anything terribly interesting. A blank window will appear, which can be moved around, resized, and closed, but it will not display anything.

In the next section, we will create some Veldrid graphics resources which we will need to render a basic multi-colored quad. In the third section, we will draw the quad and perform resource and application cleanup.

## [Next: Part 2](xref:getting-started-part2)

Here is what our application should look like at the end of this section:

```C#
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace GettingStarted
{
    class Program
    {
        private static GraphicsDevice _graphicsDevice;

        static void Main()
        {
            WindowCreateInfo windowCI = new WindowCreateInfo()
            {
                X = 100,
                Y = 100,
                WindowWidth = 960,
                WindowHeight = 540,
                WindowTitle = "Veldrid Tutorial"
            };
            Sdl2Window window = VeldridStartup.CreateWindow(ref windowCI);

            GraphicsDeviceOptions options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true
            };
            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(window, options);

            while (window.Exists)
            {
                window.PumpEvents();
            }
        }
    }
}
```