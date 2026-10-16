// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using GShader = Prowl.Graphite.Rendering.Shader;
using GTexture = Prowl.Graphite.Texture;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Texture2D = Prowl.Runtime.Resources.Texture2D;

namespace Prowl.Runtime.UI;

/// <summary>
/// Draws the GameObject UI of every scene world. World space canvases become provider items among the transparents, in
/// hierarchy order inside a canvas and back to front across canvases, and scene views also see the screen space canvases
/// laid out in world space. Screen space overlay canvases are laid out at each game view's size and drawn over its image
/// after post processing by a Ui slot feature. Everything is prepared on the main thread just before the frame is
/// submitted, once every view of the frame is known.
/// </summary>
internal static class UIRendering
{
    private static readonly Dictionary<RenderWorld, WorldUI> s_worlds = [];
    private static bool s_hooked;

    /// <summary>Adds a canvas to the UI drawn in a world. Called every frame the canvas's scene syncs.</summary>
    public static void Join(GameCanvas canvas, RenderWorld world)
    {
        if (!s_worlds.TryGetValue(world, out WorldUI? ui))
        {
            ui = new WorldUI(world);
            s_worlds[world] = ui;
            if (!s_hooked)
            {
                Graphics.BeforeSubmit += Prepare;
                s_hooked = true;
            }
        }
        ui.Canvases.Add(canvas);
    }

    public static void Leave(GameCanvas canvas, RenderWorld world)
    {
        if (!s_worlds.TryGetValue(world, out WorldUI? ui)) return;
        ui.Canvases.Remove(canvas);
        if (ui.Canvases.Count > 0) return;
        ui.Dispose();
        s_worlds.Remove(world);
    }

    private static void Prepare()
    {
        foreach (WorldUI ui in s_worlds.Values)
        {
            try { ui.Prepare(); }
            catch (Exception e) { Debug.LogErrorOnce("UIRendering.Prepare", $"Preparing the scene's UI for drawing threw: {e}"); }
        }
    }

    // ============================================================
    // One world's UI
    // ============================================================

    private sealed class WorldUI : IDisposable
    {
        public readonly HashSet<GameCanvas> Canvases = [];

        private readonly RenderWorld _world;
        private readonly WorldProvider _provider = new();
        private readonly OverlayFeature _feature;
        private readonly List<GameCanvas> _ordered = [];
        private readonly List<ViewDesc> _overlayViews = [];
        private readonly List<UIRenderItem> _items = [];
        private readonly List<(int Canvas, int Version)> _stamp = [];
        private readonly Dictionary<(uint, uint), OverlayDraws?> _sized = [];
        private readonly Dictionary<(uint, uint), OverlayGeometry> _geometry = [];
        private readonly List<(uint, uint)> _stale = [];
        private readonly Dictionary<ChildKey, ChildMaterial> _children = [];
        private readonly List<ChildKey> _staleChildren = [];

        public WorldUI(RenderWorld world)
        {
            _world = world;
            _feature = new OverlayFeature(Graphics.Renderer!);
            world.Providers.Add(_provider);
            world.Features.Add(_feature);
        }

        public void Prepare()
        {
            _overlayViews.Clear();
            bool anyView = false, sceneView = false;
            foreach (ViewDesc view in Graphics.FrameViews)
            {
                if (view.World != _world) continue;
                anyView = true;
                if (view.Kind == ViewKind.SceneView) sceneView = true;
                else if (view.Kind is ViewKind.Game or ViewKind.RenderTexture && view.LayerCount == 1) _overlayViews.Add(view);
            }

            _ordered.Clear();
            foreach (GameCanvas canvas in Canvases)
                if (canvas.IsValid() && canvas.EnabledInHierarchy) _ordered.Add(canvas);
            _ordered.Sort(static (a, b) => a.InstanceID.CompareTo(b.InstanceID));

            Float2? screenSize = GameCanvas.ScreenSizeOverride;
            bool worldSpace = GameCanvas.EditorWorldSpaceOverride;
            try
            {
                // Screen layouts first: they only copy CPU geometry, so the world layout uploaded last is what draws
                PrepareOverlays();
                PrepareWorld(anyView, sceneView);
            }
            finally
            {
                GameCanvas.ScreenSizeOverride = screenSize;
                GameCanvas.EditorWorldSpaceOverride = worldSpace;
            }
            SweepMaterials();
        }

        // ---------------- Screen space overlay ----------------

        private void PrepareOverlays()
        {
            OverlayFrame frame = new();
            _sized.Clear();
            foreach (ViewDesc view in _overlayViews)
            {
                uint width = Math.Max(1u, (uint)MathF.Round(view.Target.Width * view.Viewport.Z * view.RenderScale));
                uint height = Math.Max(1u, (uint)MathF.Round(view.Target.Height * view.Viewport.W * view.RenderScale));
                if (!_sized.TryGetValue((width, height), out OverlayDraws? draws))
                {
                    draws = BuildOverlay(width, height);
                    _sized[(width, height)] = draws;
                }
                if (draws != null)
                    frame.Views[view.ViewId] = new OverlayView(draws);
            }

            _stale.Clear();
            foreach ((uint, uint) size in _geometry.Keys)
                if (!_sized.TryGetValue(size, out OverlayDraws? used) || used == null) _stale.Add(size);
            foreach ((uint, uint) size in _stale)
            {
                _geometry[size].Release();
                _geometry.Remove(size);
            }

            _feature.Pending = frame;
        }

        private OverlayDraws? BuildOverlay(uint width, uint height)
        {
            GameCanvas.EditorWorldSpaceOverride = false;
            GameCanvas.ScreenSizeOverride = new Float2(width, height);

            _items.Clear();
            _stamp.Clear();
            foreach (GameCanvas canvas in _ordered)
            {
                if (canvas.RenderMode == RenderMode.WorldSpace) continue;
                canvas.RebuildIfDirty();
                canvas.Tree.RefreshTransforms();
                _stamp.Add((canvas.InstanceID, canvas.GeometryVersion));
                foreach (UIRenderItem item in canvas.Tree.Items)
                    if (item.Mesh.IsValid()) _items.Add(item);
            }
            if (_items.Count == 0) return null;

            // Every canvas by SortOrder, then canvas, then hierarchy
            _items.Sort(static (a, b) => a.SortKey.CompareTo(b.SortKey));

            if (!_geometry.TryGetValue((width, height), out OverlayGeometry? geometry) || !geometry.Matches(_stamp, _items.Count))
            {
                geometry?.Release();
                _geometry.Remove((width, height));
                geometry = OverlayGeometry.Build(_items, _stamp);
                if (geometry == null) return null;
                _geometry[(width, height)] = geometry;
            }

            GTexture? white = Texture2D.LoadDefault(DefaultTexture.White).GpuTexture;
            if (white == null) return null;
            Sampler clamp = Graphics.GetSampler(LinearClamp);

            OverlayDraw[] draws = new OverlayDraw[_items.Count];
            for (int i = 0; i < draws.Length; i++)
            {
                UIRenderItem item = _items[i];
                Texture2D? texture = item.Owner.GetMainTexture();
                GTexture? gpuTexture = texture.IsValid() ? texture.GpuTexture : null;
                (uint indexStart, uint indexCount, int baseVertex) = geometry.Ranges[i];
                draws[i] = new OverlayDraw
                {
                    IndexStart = indexStart,
                    IndexCount = indexCount,
                    BaseVertex = baseVertex,
                    Model = item.Model,
                    ClipToLocal = item.HasClip ? item.ClipToLocal : Float4x4.Identity,
                    ClipRect = item.HasClip ? item.ClipRect : Float4.Zero,
                    ClipParams = new Float4(item.ClipRadius, item.ClipSoftness, item.HasClip ? 1f : 0f, 0f),
                    Texture = gpuTexture ?? white,
                    View = gpuTexture != null ? texture!.DisplayView : null,
                    Sampler = gpuTexture != null ? Graphics.GetSampler(texture!.Sampler) : clamp,
                    Text = item.Owner.IsSdfText,
                };
            }
            return new OverlayDraws(geometry, draws);
        }

        // The UI's colours are display values. Colour not yet encoded for display, in a format holding linear light, takes them decoded.
        internal static bool DecodesToLinear(FeatureContext context)
            => !context.DisplayEncoded && context.Color?.Format is PixelFormat.R8_G8_B8_A8_UNorm_SRgb or PixelFormat.B8_G8_R8_A8_UNorm_SRgb
                or PixelFormat.R16_Float or PixelFormat.R16_G16_Float or PixelFormat.R16_G16_B16_A16_Float or PixelFormat.R11_G11_B10_Float
                or PixelFormat.R32_Float or PixelFormat.R32_G32_Float or PixelFormat.R32_G32_B32_A32_Float;

        // ---------------- World space ----------------

        private void PrepareWorld(bool anyView, bool sceneView)
        {
            ProviderFrame frame = _provider.Pending;
            frame.Clear();
            if (!anyView) return;

            foreach (GameCanvas canvas in _ordered)
            {
                bool world = canvas.RenderMode == RenderMode.WorldSpace;
                if (!world && !sceneView) continue;

                // Scene views show screen space canvases in world space, where they are edited
                GameCanvas.EditorWorldSpaceOverride = !world;
                canvas.RebuildIfDirty();
                canvas.Tree.RefreshTransforms();
                AddCanvas(frame, canvas, sceneViewOnly: !world);
            }
        }

        private void AddCanvas(ProviderFrame frame, GameCanvas canvas, bool sceneViewOnly)
        {
            int first = frame.Items.Count;
            AABB bounds = default;
            foreach (UIRenderItem item in canvas.Tree.Items)
            {
                Mesh mesh = item.Mesh;
                if (mesh.IsNotValid() || mesh.VertexCount == 0) continue;
                if (item.HasClip && item.ClipCoordsDirty) WriteClipCoords(item);
                item.ClipCoordsDirty = false;
                mesh.Upload();

                GpuMesh? gpuMesh = mesh.GpuMesh;
                GMaterial? material = MaterialFor(item);
                if (gpuMesh == null || material == null) continue;

                frame.Items.Add(new WorldItem
                {
                    Mesh = gpuMesh,
                    Material = material,
                    Transform = (Double4x4)item.Model,
                    Custom0 = item.HasClip ? item.ClipRect : Float4.Zero,
                    Custom1 = new Float4(item.ClipRadius, item.ClipSoftness, item.HasClip ? 1f : 0f, 0f),
                    ObjectId = (uint)item.Owner.InstanceID,
                    LayerMask = RenderableState.LayerBit(item.Owner.GameObject.LayerIndex),
                });

                AABB itemBounds = mesh.bounds.TransformBy(item.Model);
                if (frame.Items.Count == first + 1) bounds = itemBounds;
                else bounds.Encapsulate(itemBounds);
            }

            int count = frame.Items.Count - first;
            if (count == 0) return;
            Float3 center = (bounds.Min + bounds.Max) * 0.5f;
            Float3 extent = bounds.Max - bounds.Min;
            float radius = MathF.Sqrt(extent.X * extent.X + extent.Y * extent.Y + extent.Z * extent.Z) * 0.5f;
            frame.Canvases.Add(new CanvasRange(new Double3(center.X, center.Y, center.Z), radius, first, count, sceneViewOnly));
        }

        // The mesh keeps each vertex's position in its mask's space in UV1, which the material pass clips with
        private static void WriteClipCoords(UIRenderItem item)
        {
            Float4x4 toMask = item.ClipToLocal * item.Model;
            Float3[] positions = item.Mesh.Vertices;
            Float2[] coords = new Float2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Float3 p = Float4x4.TransformPoint(positions[i], toMask);
                coords[i] = new Float2(p.X, p.Y);
            }
            item.Mesh.UV2 = coords;
        }

        // The element's material with its texture as _MainTex, a child kept per texture. The tint is baked into the
        // vertex colours, so the child's own tint, tiling and offset stay neutral.
        private GMaterial? MaterialFor(UIRenderItem item)
        {
            Material material = item.Material;
            if (material.IsNotValid()) return null;
            if (!material.IsLoaded)
            {
                if (material.State == AssetState.Unloaded) _ = material.LoadAsync();
                return null;
            }
            GMaterial? parent = material.GpuMaterial;
            if (parent == null) return null;

            Texture2D? texture = item.Owner.GetMainTexture();
            GTexture? gpuTexture = texture.IsValid() ? texture.GpuTexture : null;
            if (gpuTexture == null) return parent;

            SamplerDescription sampler = texture!.Sampler;
            ChildKey key = new(parent, parent.Shader, gpuTexture, sampler);
            if (!_children.TryGetValue(key, out ChildMaterial? child))
            {
                GMaterial made = parent.CreateChild();
                GShader shader = parent.Shader;
                if (shader.FindProperty("_MainTex") >= 0) made.SetTexture("_MainTex", gpuTexture, sampler);
                if (shader.FindProperty("_MainColor") >= 0) made.SetColor("_MainColor", new Float4(1, 1, 1, 1));
                if (shader.FindProperty("_Tiling") >= 0) made.SetFloat2("_Tiling", new Float2(1, 1));
                if (shader.FindProperty("_Offset") >= 0) made.SetFloat2("_Offset", Float2.Zero);
                child = new ChildMaterial(made);
                _children[key] = child;
            }
            child.LastUsed = Graphics.FrameIndex;
            return child.Material;
        }

        private void SweepMaterials()
        {
            _staleChildren.Clear();
            foreach (KeyValuePair<ChildKey, ChildMaterial> pair in _children)
                if (pair.Key.Parent.IsDisposed || Graphics.FrameIndex - pair.Value.LastUsed > 300) _staleChildren.Add(pair.Key);
            foreach (ChildKey key in _staleChildren)
            {
                _children[key].Material.Dispose();
                _children.Remove(key);
            }
        }

        public void Dispose()
        {
            _world.Providers.Remove(_provider);
            _world.Features.Remove(_feature);
            _feature.Release();
            foreach (OverlayGeometry geometry in _geometry.Values) geometry.Release();
            _geometry.Clear();
            foreach (ChildMaterial child in _children.Values) child.Material.Dispose();
            _children.Clear();
        }
    }

    private static readonly SamplerDescription LinearClamp = SamplerDescription.Linear with
    {
        AddressModeU = SamplerAddressMode.Clamp,
        AddressModeV = SamplerAddressMode.Clamp,
        AddressModeW = SamplerAddressMode.Clamp,
    };

    private readonly record struct ChildKey(GMaterial Parent, GShader Shader, GTexture Texture, SamplerDescription Sampler);

    private sealed class ChildMaterial(GMaterial material)
    {
        public readonly GMaterial Material = material;
        public ulong LastUsed;
    }

    // ============================================================
    // World space items, a provider so a canvas keeps its hierarchy order among the transparents
    // ============================================================

    private struct WorldItem
    {
        public GpuMesh Mesh;
        public GMaterial Material;
        public Double4x4 Transform;
        public Float4 Custom0, Custom1;
        public uint ObjectId;
        public uint LayerMask;
    }

    private readonly record struct CanvasRange(Double3 Center, float Radius, int First, int Count, bool SceneViewOnly);

    private sealed class ProviderFrame
    {
        public readonly List<WorldItem> Items = [];
        public readonly List<CanvasRange> Canvases = [];

        public void Clear()
        {
            Items.Clear();
            Canvases.Clear();
        }
    }

    private sealed class WorldProvider : RenderProvider
    {
        // Filled on the main thread before the submit while the render thread may still cull the current one
        public ProviderFrame Pending = new();
        private ProviderFrame _current = new();

        public override void Extract() => (_current, Pending) = (Pending, _current);

        public override void Cull(in ProviderView view, DrawItemSink sink)
        {
            ProviderFrame frame = _current;
            bool sceneView = view.View.Kind == ViewKind.SceneView;
            Span<WorldItem> items = CollectionsMarshal.AsSpan(frame.Items);
            foreach (CanvasRange canvas in frame.Canvases)
            {
                if (canvas.SceneViewOnly && !sceneView) continue;
                if (!view.Sees(canvas.Center, canvas.Radius)) continue;

                // A canvas sorts by its distance, its items one float step apart so they draw in hierarchy order
                Double3 offset = canvas.Center - view.Camera;
                float distance = MathF.Max((float)Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z), 1e-3f);
                uint bits = BitConverter.SingleToUInt32Bits(distance);
                for (int i = 0; i < canvas.Count; i++)
                {
                    ref WorldItem item = ref items[canvas.First + i];
                    DrawItem draw = new()
                    {
                        Transform = item.Transform,
                        Custom0 = item.Custom0,
                        Custom1 = item.Custom1,
                        LayerMask = item.LayerMask,
                        ObjectId = item.ObjectId,
                        Flags = RenderableFlags.None,
                        SortDepth = BitConverter.UInt32BitsToSingle(bits + (uint)(canvas.Count - 1 - i)),
                    };
                    sink.Add(item.Mesh, 0, item.Material, draw);
                }
            }
        }
    }

    // ============================================================
    // Screen space overlay, a Ui slot feature over each game view
    // ============================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayVertex
    {
        public const uint Size = 24;

        public float X, Y, Z;
        public float U, V;
        public uint Color;
    }

    private struct OverlayDraw
    {
        public uint IndexStart, IndexCount;
        public int BaseVertex;
        public Float4x4 Model, ClipToLocal;
        public Float4 ClipRect, ClipParams;
        public GTexture Texture;
        public TextureView? View;
        public Sampler Sampler;
        public bool Text;
    }

    private sealed record OverlayDraws(OverlayGeometry Geometry, OverlayDraw[] Items);

    private sealed record OverlayView(OverlayDraws Draws);

    private sealed class OverlayFrame
    {
        public static readonly OverlayFrame Empty = new();
        public readonly Dictionary<int, OverlayView> Views = [];
    }

    /// <summary>The overlay items' geometry in one vertex and index buffer, kept while their canvases do not change.</summary>
    private sealed class OverlayGeometry
    {
        private readonly DeviceBuffer _vertices, _indices;
        private readonly (int Canvas, int Version)[] _stamp;

        public readonly VertexSource Source;
        public readonly (uint IndexStart, uint IndexCount, int BaseVertex)[] Ranges;

        private OverlayGeometry(DeviceBuffer vertices, DeviceBuffer indices, uint indexCount, (uint, uint, int)[] ranges, (int, int)[] stamp)
        {
            _vertices = vertices;
            _indices = indices;
            _stamp = stamp;
            Ranges = ranges;
            Source = new VertexSource().SetBuffer("aPosition", vertices).SetIndexBuffer(indices, Graphite.IndexFormat.UInt32, indexCount);
        }

        public bool Matches(List<(int Canvas, int Version)> stamp, int itemCount)
        {
            if (stamp.Count != _stamp.Length || itemCount != Ranges.Length) return false;
            for (int i = 0; i < _stamp.Length; i++)
                if (stamp[i] != _stamp[i]) return false;
            return true;
        }

        public static OverlayGeometry? Build(List<UIRenderItem> items, List<(int Canvas, int Version)> stamp)
        {
            GraphicsDevice? device = Graphics.Device;
            if (device == null) return null;

            int vertexCount = 0, indexCount = 0;
            foreach (UIRenderItem item in items)
            {
                vertexCount += item.Mesh.VertexCount;
                indexCount += item.Mesh.IndexCount;
            }
            if (vertexCount == 0 || indexCount == 0) return null;

            OverlayVertex[] vertices = new OverlayVertex[vertexCount];
            uint[] indices = new uint[indexCount];
            (uint, uint, int)[] ranges = new (uint, uint, int)[items.Count];
            int vertexBase = 0, indexBase = 0;
            for (int i = 0; i < items.Count; i++)
            {
                Mesh mesh = items[i].Mesh;
                Float3[] positions = mesh.Vertices;
                Float2[] uvs = mesh.UV;
                Color32[] colors = mesh.Colors32;
                uint[] meshIndices = mesh.Indices;
                for (int v = 0; v < positions.Length; v++)
                {
                    Float2 uv = v < uvs.Length ? uvs[v] : Float2.Zero;
                    Color32 c = v < colors.Length ? colors[v] : new Color32(255, 255, 255, 255);
                    vertices[vertexBase + v] = new OverlayVertex
                    {
                        X = positions[v].X, Y = positions[v].Y, Z = positions[v].Z,
                        U = uv.X, V = uv.Y,
                        Color = c.R | ((uint)c.G << 8) | ((uint)c.B << 16) | ((uint)c.A << 24),
                    };
                }
                Array.Copy(meshIndices, 0, indices, indexBase, meshIndices.Length);
                ranges[i] = ((uint)indexBase, (uint)meshIndices.Length, vertexBase);
                vertexBase += positions.Length;
                indexBase += meshIndices.Length;
            }

            DeviceBuffer vertexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)vertexCount * OverlayVertex.Size, BufferUsage.VertexBuffer));
            DeviceBuffer indexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription((uint)indexCount * sizeof(uint), BufferUsage.IndexBuffer));
            device.UpdateBuffer<OverlayVertex>(vertexBuffer, 0, vertices);
            device.UpdateBuffer<uint>(indexBuffer, 0, indices);
            return new OverlayGeometry(vertexBuffer, indexBuffer, (uint)indexCount, ranges, [.. stamp]);
        }

        public void Release()
        {
            Graphics.DeferDispose(_vertices);
            Graphics.DeferDispose(_indices);
        }
    }

    private sealed class OverlayFeature : RenderFeature
    {
        public OverlayFrame Pending = OverlayFrame.Empty;
        private OverlayFrame _current = OverlayFrame.Empty;
        private readonly FullscreenProgram _copy;
        private readonly GraphicsProgram? _program;
        private readonly PropertySet _properties = new();

        public OverlayFeature(Renderer renderer)
        {
            _copy = renderer.CreateFullscreenProgram("void main() { outColor = Graphite_SampleSource(vUV); }", "Prowl UI Overlay Copy");
            try { _program = CreateProgram(renderer.Device); }
            catch (Exception e) { Debug.LogErrorOnce("UIRendering.Overlay", $"The UI overlay shader did not compile, so screen space UI does not draw: {e}"); }
        }

        public override RenderSlot Slot => RenderSlot.Ui;
        public override ViewKinds RunsOn => ViewKinds.Game | ViewKinds.RenderTexture;

        public override void Extract() => _current = Pending;

        public override bool IsActive(in FeatureView view) => _program != null && view.LayerCount == 1 && _current.Views.ContainsKey(view.ViewId);

        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();

        public override void Render(FeatureContext context)
        {
            // The view's image first, whatever its colour target is, then the UI over it
            context.Fullscreen(_copy);
            if (_program == null || !_current.Views.TryGetValue(context.View.ViewId, out OverlayView? overlay)) return;

            CommandBuffer cmd = context.Command;
            cmd.SetShader(_program);
            cmd.SetVertexSource(overlay.Draws.Geometry.Source);
            _properties.SetFloat2("_ScreenSize", new Float2(context.View.Width, context.View.Height));
            _properties.SetInt("_Linear", WorldUI.DecodesToLinear(context) ? 1 : 0);
            foreach (OverlayDraw draw in overlay.Draws.Items)
            {
                if (draw.IndexCount == 0) continue;
                _properties.SetMatrix("_Model", draw.Model);
                _properties.SetMatrix("_ClipToLocal", draw.ClipToLocal);
                _properties.SetFloat4("_ClipRect", draw.ClipRect);
                _properties.SetFloat4("_ClipParams", draw.ClipParams);
                _properties.SetInt("_Text", draw.Text ? 1 : 0);
                if (draw.View != null) _properties.SetTexture("_MainTex", draw.View, draw.Sampler);
                else _properties.SetTexture("_MainTex", draw.Texture, draw.Sampler);
                cmd.SetProperties(_properties);
                cmd.DrawIndexed(draw.IndexCount, 1, draw.IndexStart, draw.BaseVertex, 0);
            }
        }

        private static GraphicsProgram CreateProgram(GraphicsDevice device)
        {
            ShaderDefinition definition = ShaderParser.Parse(EmbeddedResources.ReadAllText("Assets/Defaults/Shaders/UIOverlay.shader"));
            definition.Create(device, Graphics.Compiler, new Variant(), CompileMode.All);
            ShaderPass pass = definition.Passes![0];
            if (!pass.GetVariant(0).TryGetDescription(device.BackendType, out ShaderDescription description))
                throw new InvalidOperationException("The UI overlay shader did not compile.");
            description.VertexLayouts =
            [
                new VertexLayoutDescription(0, OverlayVertex.Size,
                    new VertexElementDescription("aPosition", VertexElementFormat.Float3, 0),
                    new VertexElementDescription("aTexCoord", VertexElementFormat.Float2, 12),
                    new VertexElementDescription("aColor", VertexElementFormat.Byte4_Norm, 20))
            ];
            description.BlendState = pass.State.ToBlendState(BlendStateDescription.SingleDisabled);
            description.DepthStencilState = pass.State.ToDepthStencilState(DepthStencilStateDescription.Disabled);
            description.RasterizerState = pass.State.ToRasterizerState(new(FaceCullMode.None, FrontFace.Clockwise, true, false));
            GraphicsProgram program = device.ResourceFactory.CreateGraphicsProgram(description);
            program.Name = "Prowl UI Overlay";
            return program;
        }

        public void Release()
        {
            Graphics.DeferDispose(_program);
            Graphics.DeferDispose(_copy);
        }
    }
}
