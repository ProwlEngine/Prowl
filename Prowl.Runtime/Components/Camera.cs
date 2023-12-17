using Prowl.Icons;
using Prowl.Runtime.Resources.RenderPipeline;
using Prowl.Runtime.SceneManagement;
using Raylib_cs;
using System;
using Shader = Prowl.Runtime.Shader;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Camera}  Camera")]
public class Camera : MonoBehaviour
{
    public static Camera? Current;

    public AssetRef<RenderPipeline> RenderPipeline;
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

    public event Action<int, int> Resize;

    public AssetRef<RenderTexture> Target;

    public GBuffer gBuffer { get; private set; }

    public enum DebugDraw { Off, Albedo, Normals, Depth, Velocity, ObjectID }
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
            gBuffer = new GBuffer((int)size.X, (int)size.Y);
            Resize?.Invoke(gBuffer.Width, gBuffer.Height);
        }
        else if (gBuffer.Width != (int)size.X || gBuffer.Height != (int)size.Y)
        {
            gBuffer.UnloadGBuffer();
            gBuffer = new GBuffer((int)size.X, (int)size.Y);
            Resize?.Invoke(gBuffer.Width, gBuffer.Height);
        }
    }

    internal void RenderAllOfOrder(RenderingOrder order)
    {
        foreach (var go in SceneManager.AllGameObjects)
            if (go.EnabledInHierarchy)
                foreach (var comp in go.GetComponents())
                    if (comp.Enabled && comp.RenderOrder == order)
                        comp.Internal_OnRenderObject();
    }

    public void DrawFullScreenTexture(Raylib_cs.Texture2D texture)
    {
        Rlgl.rlDisableDepthMask();
        Rlgl.rlDisableDepthTest();
        Rlgl.rlDisableBackfaceCulling();

        var s = GetRenderTargetSize() * 1.0f;
        //var s = GetRenderTargetSize();
        Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.width, -texture.height), new Rectangle(0, 0, (float)s.X, (float)s.Y), System.Numerics.Vector2.Zero, 0.0f, Color.white);

        Rlgl.rlEnableDepthMask();
        Rlgl.rlEnableDepthTest();
        Rlgl.rlEnableBackfaceCulling();
    }

    private void OpaquePass()
    {
        gBuffer.Begin();                            // Start
        RenderAllOfOrder(RenderingOrder.Opaque);    // Render
        gBuffer.End();                              // End
    }

    Matrix4x4? oldView = null;
    Matrix4x4? oldProjection = null;

    public Matrix4x4 View => Matrix4x4.CreateLookToLeftHanded(Vector3.Zero, GameObject.Forward, GameObject.Up);

    public void Render(int width, int height)
    {
        var rp = RenderPipeline;
        if (rp.IsAvailable == false) 
        {
            rp = Application.AssetProvider.LoadAsset<RenderPipeline>("Defaults/DefaultRenderPipeline.scriptobj");
            if (rp.IsAvailable == false)
            {
                Debug.LogError($"Camera on {GameObject.Name} cannot render, Missing Default Render Pipeline!");
                return;
            }
        }

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
        Graphics.MatProjection = Current.GetProjectionMatrix(width, height);
        Graphics.OldMatView = oldView ?? Graphics.MatView;
        Graphics.OldMatProjection = oldProjection ?? Graphics.MatProjection;

        // Set default jitter to false, this is set to true in a TAA pass
        Graphics.UseJitter = false;
        foreach (var node in rp.Res!.nodes) {
            if (node is RenderPassNode renderPass)
                renderPass.PreRender();
        }

        Graphics.MatOldViewTransposed = Matrix4x4.Transpose(Graphics.OldMatView);
        Graphics.MatOldProjectionTransposed = Matrix4x4.Transpose(Graphics.OldMatProjection);
        Graphics.MatViewTransposed = Matrix4x4.Transpose(Graphics.MatView);
        Graphics.MatProjectionTransposed = Matrix4x4.Transpose(Graphics.MatProjection);
        Matrix4x4.Invert(Graphics.MatView, out Graphics.MatViewInverse);
        Graphics.MatViewInverseTransposed = Matrix4x4.Transpose(Graphics.MatViewInverse);
        Matrix4x4.Invert(Graphics.MatProjection, out Graphics.MatProjectionInverse);
        Graphics.MatProjectionInverseTransposed = Matrix4x4.Transpose(Graphics.MatProjectionInverse);

        CheckGBuffer();

        OpaquePass();

#warning TODO: Smarter Shadowmap Updating, updating every frame for every camera is stupid
        Graphics.UpdateAllShadowmaps();

        var outputNode = rp.Res!.GetNode<OutputNode>();
        if (outputNode == null)
        {
            EarlyEndRender();

            Debug.LogError("RenderPipeline has no OutputNode!");
            return;
        }

        RenderTexture result = (RenderTexture)rp.Res!.GetNode<OutputNode>().GetValue(null);

        if (result == null)
        {
            EarlyEndRender();

            Debug.LogError("RenderPipeline OutputNode failed to return a RenderTexture!");
            return;
        }

        //LightingPass();
        //
        //PostProcessStagePreCombine?.Invoke(gBuffer);
        //
        //if (debugDraw == DebugDraw.Off)
        //    CombinePass();
        //
        //PostProcessStagePostCombine?.Invoke(gBuffer);

        // Draw to Screen
        Target.Res?.Begin();
        if (DoClear) Raylib.ClearBackground(ClearColor);
        
        if (debugDraw == DebugDraw.Off)
        {
            DrawFullScreenTexture(result.InternalTextures[0]);
        }
        else if (debugDraw == DebugDraw.Albedo)
            DrawFullScreenTexture(gBuffer.AlbedoAO);
        else if (debugDraw == DebugDraw.Normals)
            DrawFullScreenTexture(gBuffer.NormalMetallic);
        else if (debugDraw == DebugDraw.Depth)
            DrawFullScreenTexture(gBuffer.PositionRoughness);
        else if (debugDraw == DebugDraw.Velocity)
            DrawFullScreenTexture(gBuffer.Velocity);
        else if (debugDraw == DebugDraw.ObjectID)
            DrawFullScreenTexture(gBuffer.ObjectIDs);
        
        Target.Res?.End();

        Current = null;
        Rlgl.rlSetBlendMode(BlendMode.BLEND_ALPHA);

        oldView = Graphics.MatView;
        oldProjection = Graphics.MatProjection;
    }

    private void EarlyEndRender()
    {
        if (DoClear)
        {
            Target.Res?.Begin();
            Raylib.ClearBackground(ClearColor);
            Target.Res?.End();
        }
        Current = null;
        Rlgl.rlSetBlendMode(BlendMode.BLEND_ALPHA);
    }
}
