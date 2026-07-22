// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Tests that GPU-backed resources can be created and used without a graphics device (headless /
/// dedicated server). GPU command submission is a no-op when <see cref="Application.IsHeadless"/>, so
/// constructing textures, materials, meshes, etc. must not crash - they just don't upload to a GPU.
/// </summary>
public class HeadlessGraphicsTests
{
    [Fact]
    public void Graphics_ReportsHeadless_WhenNoDevice()
    {
        // The test process never initializes a GL device.
        Assert.True(Application.IsHeadless);
    }

    [Fact]
    public void Texture2D_CreatesHeadless_WithoutThrowing()
    {
        var tex = new Texture2D(64, 64);
        Assert.Equal(64u, tex.Width);
        Assert.Equal(64u, tex.Height);
    }

    [Fact]
    public void Texture2D_LargeSize_DoesNotFailValidationHeadless()
    {
        // Capability constants default to sane minimums so size validation passes pre-device.
        var tex = new Texture2D(4096, 4096);
        Assert.Equal(4096u, tex.Width);
    }

    [Fact]
    public void Material_CreatesHeadless_WithoutThrowing()
    {
        // Material's ctor loads the default shader, which parses a default texture - the exact path
        // that used to crash headless when texture creation hit an uninitialized GL device.
        var mat = new Material();
        // Assert the default-shader load actually completed (that's the path that used to crash
        // headless), not merely that the ctor returned non-null.
        Assert.NotNull(mat.Shader);
    }

    [Fact]
    public void Mesh_UploadHeadless_DoesNotThrow()
    {
        var mesh = Mesh.CreateCube(Vector.Float3.One);
        // Upload encodes GPU buffer-creation command buffers; headless drops them on the floor.
        mesh.Upload();
        Assert.True(mesh.VertexCount > 0);
    }
}
