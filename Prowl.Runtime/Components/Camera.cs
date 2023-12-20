using Prowl.Icons;
using Prowl.Runtime.Resources.RenderPipeline;
using Prowl.Runtime.SceneManagement;
using Silk.NET.OpenGL;
using System;

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
        return new Vector2(Window.InternalWindow.FramebufferSize.X, Window.InternalWindow.FramebufferSize.Y);
    }

    private void CheckGBuffer()
    {
        RenderResolution = Math.Clamp(RenderResolution, 0.1f, 4.0f);

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
        if (RenderPipeline.IsAvailable == false) 
        {
            RenderPipeline = Application.AssetProvider.LoadAsset<RenderPipeline>("Defaults/DefaultRenderPipeline.scriptobj");
            if (RenderPipeline.IsAvailable == false)
            {
                Debug.LogError($"Camera on {GameObject.Name} cannot render, Missing Default Render Pipeline!");
                return;
            }
        }

        var rp = RenderPipeline;
        if (Target.IsAvailable)
        {
            width = Target.Res!.Width;
            height = Target.Res!.Height;
        }
        else if (width == -1 || height == -1)
        {
            width = Window.InternalWindow.FramebufferSize.X;
            height = Window.InternalWindow.FramebufferSize.Y;
        }

        width = (int)(width * RenderResolution);
        height = (int)(height * RenderResolution);

        CheckGBuffer();

        
        Current = this;

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

        Matrix4x4.Invert(Graphics.MatView, out Graphics.MatViewInverse);
        Matrix4x4.Invert(Graphics.MatProjection, out Graphics.MatProjectionInverse);

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


        if (debugDraw == DebugDraw.Off) {
            Graphics.Blit(Target.Res ?? null, result.InternalTextures[0], DoClear);
        }
        else if (debugDraw == DebugDraw.Albedo)
            Graphics.Blit(Target.Res ?? null, gBuffer.AlbedoAO, DoClear);
        else if (debugDraw == DebugDraw.Normals)
            Graphics.Blit(Target.Res ?? null, gBuffer.NormalMetallic, DoClear);
        else if (debugDraw == DebugDraw.Depth)
            Graphics.Blit(Target.Res ?? null, gBuffer.Depth, DoClear);
        else if (debugDraw == DebugDraw.Velocity)
            Graphics.Blit(Target.Res ?? null, gBuffer.Velocity, DoClear);
        else if (debugDraw == DebugDraw.ObjectID)
            Graphics.Blit(Target.Res ?? null, gBuffer.ObjectIDs, DoClear);

        Current = null;

        oldView = Graphics.MatView;
        oldProjection = Graphics.MatProjection;
    }

    private void EarlyEndRender()
    {
        if (DoClear)
        {
            Target.Res?.Begin();
            Graphics.Clear(ClearColor.r, ClearColor.g, ClearColor.b, ClearColor.a);
            Target.Res?.End();
        }
        Current = null;
        Graphics.GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }
}
