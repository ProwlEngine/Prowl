using Prowl.Icons;
using Prowl.Runtime.Resources;
using Prowl.Runtime.SceneManagement;
using Raylib_cs;
using System;
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

    public AssetRef<RenderTexture> Target;

    public GBuffer gBuffer { get; private set; }
    Resources.Material CombineShader;

    public enum DebugDraw { Off, Diffuse, Normals, Depth, Lighting, Velocity }
    public DebugDraw debugDraw = DebugDraw.Off;

    public Matrix4x4 GetProjectionMatrix(float width, float height)
    {
        if (projectionType == ProjectionType.Orthographic)
            return System.Numerics.Matrix4x4.CreateOrthographicLeftHanded(width, height, NearClip, FarClip).ToDouble();
        else
            return System.Numerics.Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView.ToRad(), width / height, NearClip, FarClip).ToDouble();
    }


    private Vector2 GetRenderTargetSize()
    {
        if (Target.IsAvailable) return new Vector2(Target.Res!.Width, Target.Res!.Height);
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
        Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.width, -texture.height), new Rectangle(0, 0, (float)s.X, (float)s.Y), System.Numerics.Vector2.Zero, 0.0f, Color.white);
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

    Matrix4x4? oldView = null;
    Matrix4x4? oldProjection = null;

    public Matrix4x4 View => Matrix4x4.CreateLookToLeftHanded(Vector3.Zero, GameObject.Forward, GameObject.Up);

    public void Render(int width, int height)
    {
        if (Target.IsAvailable)
        {
            width = Target.Res!.Width;
            height = Target.Res!.Height;
        }
        else if (width == -1 || height == -1)
        {
            width = Rlgl.rlGetFramebufferWidth();
            height = Rlgl.rlGetFramebufferHeight();
        }

        width = (int)(width * RenderResolution);
        height = (int)(height * RenderResolution);

        Rlgl.rlSetBlendMode(BlendMode.BLEND_ADD_COLORS);
        Current = this;
        Graphics.Resolution = new Vector2(width, height);
        Graphics.MatView = View;
        Graphics.MatProjection = Camera.Current.GetProjectionMatrix(width, height);
        Graphics.OldMatView = oldView ?? Graphics.MatView;
        Graphics.OldMatProjection = oldProjection ?? Graphics.MatProjection;

        Graphics.OldMatViewTransposed = Matrix4x4.Transpose(Graphics.OldMatView);
        Graphics.OldMatProjectionTransposed = Matrix4x4.Transpose(Graphics.OldMatProjection);
        Graphics.MatViewTransposed = Matrix4x4.Transpose(Graphics.MatView);
        Graphics.MatProjectionTransposed = Matrix4x4.Transpose(Graphics.MatProjection);
        Matrix4x4.Invert(Graphics.MatView, out Graphics.MatViewInverse);
        Graphics.MatViewInverseTransposed = Matrix4x4.Transpose(Graphics.MatViewInverse);

        CheckGBuffer();

        OpaquePass();

#warning TODO: Smarter Shadowmap Updating, updating every frame for every camera is stupid
        Graphics.UpdateAllShadowmaps();

        LightingPass();

        PostProcessStagePreCombine?.Invoke(gBuffer);

        if (debugDraw == DebugDraw.Off)
            CombinePass();

        PostProcessStagePostCombine?.Invoke(gBuffer);

        // Draw to Screen
        Target.Res?.Begin();
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

        Target.Res?.End();

        Current = null;
        Rlgl.rlSetBlendMode(BlendMode.BLEND_ALPHA);

        oldView = Graphics.MatView;
        oldProjection = Graphics.MatProjection;
    }

}
