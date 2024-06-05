using Prowl.Icons;
using Prowl.Runtime.Rendering.Primitives;
using Prowl.Runtime.Resources.RenderPipeline;
using Prowl.Runtime.SceneManagement;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Camera}  Camera")]
[ExecuteAlways]
public class Camera : MonoBehaviour
{
    public static Camera? Current;

    public AssetRef<RenderPipeline> RenderPipeline;
    public bool DoClear = true;
    public Color ClearColor = new Color(0f, 0f, 0f, 1f);
    public float FieldOfView = 60f;
    public float OrthographicSize = 0.5f;
    public int DrawOrder = 0;
    public float NearClip = 0.01f;
    public float FarClip = 1000f;

    public float RenderResolution = 1f;

    public bool ShowGizmos = false;

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
            //return System.Numerics.Matrix4x4.CreateOrthographicLeftHanded(width, height, NearClip, FarClip).ToDouble();
            return System.Numerics.Matrix4x4.CreateOrthographicOffCenterLeftHanded(-OrthographicSize, OrthographicSize, -OrthographicSize, OrthographicSize, NearClip, FarClip).ToDouble();
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
            gBuffer = new GBuffer((int)size.x, (int)size.y);
            Resize?.Invoke(gBuffer.Width, gBuffer.Height);
        }
        else if (gBuffer.Width != (int)size.x || gBuffer.Height != (int)size.y)
        {
            gBuffer.UnloadGBuffer();
            gBuffer = new GBuffer((int)size.x, (int)size.y);
            Resize?.Invoke(gBuffer.Width, gBuffer.Height);
        }
    }

    internal void RenderAllOfOrder(RenderingOrder order)
    {
        foreach (var go in SceneManager.AllGameObjects)
            if (go.enabledInHierarchy)
                foreach (var comp in go.GetComponents())
                    if (comp.Enabled && comp.RenderOrder == order)
                        comp.OnRenderObject();
    }

    private void OpaquePass()
    {
        SceneManager.ForeachComponent((x) => x.Do(x.OnPreRender));
        gBuffer.Begin();                            // Start
        RenderAllOfOrder(RenderingOrder.Opaque);    // Render
        gBuffer.End();                              // End
        SceneManager.ForeachComponent((x) => x.Do(x.OnPostRender));
    }

    Matrix4x4? oldView = null;
    Matrix4x4? oldProjection = null;

    public Matrix4x4 View => Matrix4x4.CreateLookToLeftHanded(Vector3.zero, GameObject.Transform.forward, GameObject.Transform.up);

    public void Render(int width, int height)
    {
        if (RenderPipeline.IsAvailable == false)
        {
            Debug.LogError($"Camera on {GameObject.Name} has no RenderPipeline assigned, Falling back to default.");
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
        rp.Res!.Prepare("Deferred", width, height);

        Matrix4x4.Invert(Graphics.MatProjection, out Graphics.MatProjectionInverse);

        OpaquePass();

        var outputNode = rp.Res!.GetNode<OutputNode>();
        if (outputNode == null)
        {
            EarlyEndRender();

            Debug.LogError("RenderPipeline has no OutputNode!");
            return;
        }

        RenderTexture? result = rp.Res!.Render();

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
        if (debugDraw == DebugDraw.Off)
        {
            Graphics.Blit(Target.Res ?? null, result.InternalTextures[0], DoClear);
            Graphics.BlitDepth(gBuffer.buffer, Target.Res ?? null);
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

        oldView = Graphics.MatView;
        oldProjection = Graphics.MatProjection;

        if (ShowGizmos)
        {
            Target.Res?.Begin();
            if (Graphics.UseJitter)
                Graphics.MatProjection = Current.GetProjectionMatrix(width, height); // Cancel out jitter if there is any
            Gizmos.Render();
            Target.Res?.End();
        }
#warning TODO: Atm gizmos is handled in a RenderObject method, but it should all be inside Update() including rendering but that will happen over the rendering overhaul, so for now reset every render (when rendering overhaul, it should be once per frame)
        Gizmos.Clear();

        Current = null;
        Graphics.UseJitter = false;
    }

    private void EarlyEndRender()
    {
        Graphics.UseJitter = false;
        if (DoClear)
        {
            Target.Res?.Begin();
            Graphics.Clear(ClearColor.r, ClearColor.g, ClearColor.b, ClearColor.a);
            Target.Res?.End();
        }
        Current = null;
    }

    public override void LateUpdate()
    {
        UpdateCachedRT();
    }

    public override void OnDisable()
    {
        gBuffer?.UnloadGBuffer();

        // Clear the Cached RenderTextures
        foreach (var (name, (renderTexture, frameCreated)) in cachedRenderTextures)
            renderTexture.Destroy();
        cachedRenderTextures.Clear();
    }

    public Ray ScreenPointToRay(Vector2 screenPoint)
    {
        // Get the render target size
        Vector2 renderTargetSize = GetRenderTargetSize();

        // Normalize screen coordinates to [-1, 1]
        Vector2 ndc = new Vector2(
            (screenPoint.x / renderTargetSize.x) * 2.0f - 1.0f,
            1.0f - (screenPoint.y / renderTargetSize.y) * 2.0f
        );

        // Create the near and far points in NDC
        Vector4 nearPointNDC = new Vector4(ndc.x, ndc.y, 0.0f, 1.0f);
        Vector4 farPointNDC = new Vector4(ndc.x, ndc.y, 1.0f, 1.0f);

        // Get the view and projection matrices
        Matrix4x4 viewMatrix = Matrix4x4.CreateLookToLeftHanded(GameObject.Transform.position, GameObject.Transform.forward, GameObject.Transform.up);
        Matrix4x4 projectionMatrix = GetProjectionMatrix((int)renderTargetSize.x, (int)renderTargetSize.y);

        // Calculate the inverse view-projection matrix
        Matrix4x4 viewProjectionMatrix = viewMatrix * projectionMatrix;
        Matrix4x4.Invert(viewProjectionMatrix, out Matrix4x4 inverseViewProjectionMatrix);

        // Unproject the near and far points to world space
        Vector4 nearPointWorld = Vector4.Transform(nearPointNDC, inverseViewProjectionMatrix);
        Vector4 farPointWorld = Vector4.Transform(farPointNDC, inverseViewProjectionMatrix);

        // Perform perspective divide
        nearPointWorld /= nearPointWorld.w;
        farPointWorld /= farPointWorld.w;

        // Create the ray
        Vector3 rayOrigin = new Vector3(nearPointWorld.x, nearPointWorld.y, nearPointWorld.z);
        Vector3 rayDirection = Vector3.Normalize(new Vector3(farPointWorld.x, farPointWorld.y, farPointWorld.z) - rayOrigin);

        return new Ray(rayOrigin, rayDirection);
    }


    #region RT Cache

    private readonly Dictionary<string, (RenderTexture, long frameCreated)> cachedRenderTextures = [];
    private const int MaxUnusedFrames = 10;

    public RenderTexture GetCachedRT(string name, int width, int height, TextureImageFormat[] format)
    {
        if (cachedRenderTextures.ContainsKey(name))
        {
            // Update the frame created
            var cached = cachedRenderTextures[name];
            cachedRenderTextures[name] = (cached.Item1, Time.frameCount);
            return cached.Item1;
        }
        RenderTexture rt = new(width, height, 1, false, format);
        rt.Name = name;
        cachedRenderTextures[name] = (rt, Time.frameCount);
        return rt;
    }

    public void UpdateCachedRT()
    {
        var disposableTextures = new List<(RenderTexture, string)>();
        foreach (var (name, (renderTexture, frameCreated)) in cachedRenderTextures)
            if (Time.frameCount - frameCreated > MaxUnusedFrames)
                disposableTextures.Add((renderTexture, name));

        foreach (var renderTexture in disposableTextures)
        {
            cachedRenderTextures.Remove(renderTexture.Item2);
            renderTexture.Item1.Destroy();
        }
    }

    #endregion
}
