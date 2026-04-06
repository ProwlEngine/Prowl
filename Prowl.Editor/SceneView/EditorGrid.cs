using System;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Draws an infinite-looking XZ grid in the editor scene view using the pristine grid shader.
/// Creates a large plane mesh centered on the camera and renders it with alpha blending.
/// </summary>
public class EditorGrid
{
    private Mesh? _gridMesh;
    private Material? _gridMaterial;

    private const float GridExtent = 500f; // Half-size of the grid plane

    public Color GridColor { get; set; } = new Color(0.5f, 0.5f, 0.5f, 0.3f);
    public float PrimaryGridSize { get; set; } = 1f;    // 1 line per meter
    public float SecondaryGridSize { get; set; } = 0.25f; // 1 line per 4 meters
    public float LineWidth { get; set; } = 0.02f;
    public float Falloff { get; set; } = 1.5f;
    public float MaxDistance { get; set; } = 500f;

    /// <summary>
    /// Queue the grid for rendering. Call before the pipeline renders.
    /// The grid plane is re-centered on the camera's XZ position each frame.
    /// </summary>
    public void Draw(Scene scene, Float3 cameraPosition)
    {
        EnsureResources();
        if (_gridMesh == null || _gridMaterial == null) return;

        // Center the grid plane on the camera's XZ position (snapped to grid)
        float cx = MathF.Round(cameraPosition.X / PrimaryGridSize) * PrimaryGridSize;
        float cz = MathF.Round(cameraPosition.Z / PrimaryGridSize) * PrimaryGridSize;
        var transform = Float4x4.CreateTranslation(new Float3(cx, 0, cz));

        // Set material properties
        _gridMaterial.SetColor("_GridColor", GridColor);
        _gridMaterial.SetFloat("_PrimaryGridSize", PrimaryGridSize);
        _gridMaterial.SetFloat("_SecondaryGridSize", SecondaryGridSize);
        _gridMaterial.SetFloat("_LineWidth", LineWidth);
        _gridMaterial.SetFloat("_Falloff", Falloff);
        _gridMaterial.SetFloat("_MaxDist", MaxDistance);

        Graphics.DrawMesh(scene, _gridMesh, transform, _gridMaterial);
    }

    private void EnsureResources()
    {
        if (_gridMesh == null)
        {
            // Large XZ plane
            float e = GridExtent;
            _gridMesh = new Mesh();
            _gridMesh.Vertices =
            [
                new Float3(-e, 0, -e),
                new Float3( e, 0, -e),
                new Float3( e, 0,  e),
                new Float3(-e, 0,  e),
            ];
            _gridMesh.UV =
            [
                new Float2(-e, -e),
                new Float2( e, -e),
                new Float2( e,  e),
                new Float2(-e,  e),
            ];
            _gridMesh.Normals =
            [
                Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY,
            ];
            _gridMesh.Indices = [0, 2, 1, 0, 3, 2];
            _gridMesh.RecalculateBounds();
            _gridMesh.Upload();
        }

        if (_gridMaterial == null)
        {
            var shader = Shader.LoadDefault(DefaultShader.Grid);
            if (shader != null)
                _gridMaterial = new Material(shader);
        }
    }
}
