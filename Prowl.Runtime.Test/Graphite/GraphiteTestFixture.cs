// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Silk.NET.GLFW;
using Silk.NET.OpenGL;

using Prowl.Runtime.Graphite;
using Prowl.Runtime.Graphite.OpenGL;

namespace Prowl.Runtime.Test.Graphite;

/// <summary>
/// Test fixture that creates an OpenGL context for Graphite API testing.
/// </summary>
public class GraphiteTestFixture : IDisposable
{
    private readonly Glfw _glfw;
    private readonly unsafe WindowHandle* _window;
    private bool _disposed;

    public GL GL { get; }
    public GLGraphiteDevice Device { get; }

    public unsafe GraphiteTestFixture()
    {
        _glfw = Glfw.GetApi();

        if (!_glfw.Init())
            throw new InvalidOperationException("Failed to initialize GLFW");

        // Request OpenGL 4.3+ core profile
        _glfw.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        _glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        _glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        _glfw.WindowHint(WindowHintBool.Visible, false); // Hidden window for tests

        _window = _glfw.CreateWindow(800, 600, "Graphite Test", null, null);
        if (_window == null)
        {
            _glfw.Terminate();
            throw new InvalidOperationException("Failed to create GLFW window");
        }

        _glfw.MakeContextCurrent(_window);

        // Create GL context
        GL = GL.GetApi(_glfw.GetProcAddress);

        // Set static GL reference BEFORE initializing the Graphite device
        Prowl.Runtime.GraphicsBackend.OpenGL.GLDevice.GL = GL;

        // Initialize the Graphite device
        Device = new GLGraphiteDevice();
        Device.Initialize(new GraphiteDeviceOptions { EnableDebugLayer = true });
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Device?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Device already disposed, ignore
        }

        if (_window != null)
        {
            _glfw.DestroyWindow(_window);
        }
        _glfw.Terminate();
    }
}

/// <summary>
/// Collection definition for tests that share the OpenGL context.
/// </summary>
[Xunit.CollectionDefinition("Graphite")]
public class GraphiteTestCollection : Xunit.ICollectionFixture<GraphiteTestFixture>
{
}
