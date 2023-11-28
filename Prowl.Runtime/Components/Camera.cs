using Prowl.Icons;
using Prowl.Runtime.Resources;
using Prowl.Runtime.SceneManagement;
using Raylib_cs;
using System;
using System.Numerics;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Components;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Camera}  Camera")]
public class Camera : MonoBehaviour
{
    public static Camera? Current;

    public bool DoClear = true;
    public Color ClearColor = new Color(0f, 0f, 0f, 1f);
    public float FieldOfView = 60f;
    public int DrawOrder = 0;
    public float NearClip = 0.01f;
    public float FarClip = 1000f;

    public float Contrast = 1.1f;
    public float Saturation = 1.2f;

    public float RenderResolution = 1f;

    public enum ProjectionType { Perspective, Orthographic }
    public ProjectionType projectionType = ProjectionType.Perspective;

    public event Action<GBuffer> PostProcessStagePreCombine;
    public event Action<GBuffer> PostProcessStagePostCombine;
    public event Action<int, int> Resize;

    public RenderTexture Target;

    public GBuffer gBuffer { get; private set; }
    Resources.Material CombineShader;

    public enum DebugDraw { Off, Diffuse, Normals, Depth, Lighting, Velocity }
    public DebugDraw debugDraw = DebugDraw.Off;

    public Matrix4x4 GetProjectionMatrix(float width, float height)
    {
        if (projectionType == ProjectionType.Orthographic)
            return Matrix4x4.CreateOrthographicLeftHanded(width, height, NearClip, FarClip);
        else
            return Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView.ToRad(), width / height, NearClip, FarClip);
    }


    private Vector2 GetRenderTargetSize()
    {
        if (Target != null) return new Vector2(Target.Width, Target.Height);
        return new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
    }

    private void CheckGBuffer()
    {
        RenderResolution = Math.Clamp(RenderResolution, 0.1f, 8.0f);

        Vector2 size = GetRenderTargetSize() * RenderResolution;
        if (gBuffer == null)
        {
            gBuffer = new GBuffer((int)size.X, (int)size.Y, RenderResolution);
            Resize?.Invoke(gBuffer.Width, gBuffer.Height);
        }
        else if (gBuffer.Width != (int)size.X || gBuffer.Height != (int)size.Y)
        {
            gBuffer.UnloadGBuffer();
            gBuffer = new GBuffer((int)size.X, (int)size.Y, RenderResolution);
            Resize?.Invoke(gBuffer.Width, gBuffer.Height);
        }
    }

    private void RenderAllOfOrder(RenderingOrder order)
    {
        foreach (var go in GameObjectManager.AllGameObjects)
            if (go.EnabledInHierarchy)
                foreach (var comp in go.GetComponents())
                    if (comp.Enabled && comp.RenderOrder == order)
                        comp.Internal_OnRenderObject();
    }

    public void DrawFullScreenTexture(Raylib_cs.Texture2D texture)
    {
        var s = GetRenderTargetSize() * 1.0f;
        //var s = GetRenderTargetSize();
        Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.width, -texture.height), new Rectangle(0, 0, s.X, s.Y), Vector2.Zero, 0.0f, Color.white);
    }

    private void OpaquePass()
    {
        gBuffer.Begin();                            // Start
        RenderAllOfOrder(RenderingOrder.Opaque);    // Render
        gBuffer.End();                              // End
    }

    private void LightingPass()
    {
        gBuffer.BeginLighting();
        RenderAllOfOrder(RenderingOrder.Lighting);
        gBuffer.EndLighting();
    }

    private void CombinePass()
    {
        gBuffer.BeginCombine();
        CombineShader ??= new(Shader.Find("Defaults/GBuffercombine.shader"));
        //CombineShader.mpb.Clear();
        CombineShader.SetTexture("gAlbedoAO", gBuffer.AlbedoAO);
        CombineShader.SetTexture("gLighting", gBuffer.Lighting);
        CombineShader.SetFloat("Contrast", Math.Clamp(Contrast, 0, 2));
        CombineShader.SetFloat("Saturation", Math.Clamp(Saturation, 0, 2));
        CombineShader.EnableKeyword("ACESTONEMAP");
        CombineShader.EnableKeyword("GAMMACORRECTION");
        CombineShader.SetPass(0, true);
        //CombineShader.Begin();
        DrawFullScreenTexture(gBuffer.Lighting);
        //CombineShader.End();
        CombineShader.EndPass();
        gBuffer.EndCombine();
    }

    public void Render()
    {
        Rlgl.rlSetBlendMode(BlendMode.BLEND_ADD_COLORS);
        Current = this;
        Graphics.OldMatView = Graphics.MatView;
        Graphics.OldMatProjection = Graphics.MatProjection;
        Graphics.MatView = Camera.Current.GameObject.View;
        Graphics.MatProjection = Camera.Current.GetProjectionMatrix(Rlgl.rlGetFramebufferWidth(), Rlgl.rlGetFramebufferHeight());

        Graphics.OldMatViewTransposed = Matrix4x4.Transpose(Graphics.OldMatView);
        Graphics.OldMatProjectionTransposed = Matrix4x4.Transpose(Graphics.OldMatProjection);
        Graphics.MatViewTransposed = Matrix4x4.Transpose(Graphics.MatView);
        Graphics.MatProjectionTransposed = Matrix4x4.Transpose(Graphics.MatProjection);
        Matrix4x4.Invert(Graphics.MatView, out Graphics.MatViewInverse);
        Graphics.MatViewInverseTransposed = Matrix4x4.Transpose(Graphics.MatViewInverse);

        CheckGBuffer();

        OpaquePass();

        Graphics.UpdateAllShadowmaps();

        LightingPass();

        PostProcessStagePreCombine?.Invoke(gBuffer);

        if (debugDraw == DebugDraw.Off)
            CombinePass();

        PostProcessStagePostCombine?.Invoke(gBuffer);

        // Draw to Screen
        Target?.Begin();
        if (DoClear) Raylib.ClearBackground(ClearColor);

        if(debugDraw == DebugDraw.Off)
        {
            DrawFullScreenTexture(gBuffer.Combined);
        }
        else if(debugDraw == DebugDraw.Diffuse) 
            DrawFullScreenTexture(gBuffer.AlbedoAO);
        else if (debugDraw == DebugDraw.Normals) 
            DrawFullScreenTexture(gBuffer.NormalMetallic);
        else if (debugDraw == DebugDraw.Depth) 
            DrawFullScreenTexture(gBuffer.PositionRoughness);
        else if (debugDraw == DebugDraw.Lighting)
            DrawFullScreenTexture(gBuffer.Lighting);
        else if (debugDraw == DebugDraw.Velocity)
            DrawFullScreenTexture(gBuffer.Velocity);

        Prowl.Runtime.Draw.Render(this);                // Gizmos/Debug Draw

        Target?.End();

        Current = null;
        Rlgl.rlSetBlendMode(BlendMode.BLEND_ALPHA);
    }

}
