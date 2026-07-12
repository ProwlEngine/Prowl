using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Scribe;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region Fields & Properties

        // Layout and hierarchy management
        private ElementHandle _rootElementHandle;
        internal Stack<ElementHandle> _elementStack = new Stack<ElementHandle>();
        private readonly Stack<int> _IDStack = new Stack<int>();
        private readonly HashSet<int> _createdElements = new HashSet<int>();

        private readonly Dictionary<int, Hashtable> _storage = new Dictionary<int, Hashtable>();

        // Reused across frames so the deferred-layer render doesn't allocate a dictionary + buckets
        // every frame. Buckets are pooled and returned as each layer drains.
        private readonly SortedDictionary<int, List<(ElementHandle handle, Transform2D transform)>> _deferredRender = new();
        private readonly Stack<List<(ElementHandle handle, Transform2D transform)>> _deferredBucketPool = new();
        // Scratch list reused by EndOfFrameCleanupStorage (avoids _storage.Keys.ToArray() each frame).
        private readonly List<int> _storageCleanupScratch = new List<int>();

        // Rendering context
        private Canvas _canvas;
        private ICanvasRenderer _renderer;
        private float _width;
        private float _height;
        private Stopwatch _timer = new Stopwatch();

        // Built-in developer tools. Off unless DevTools.Enabled is set; then F12 toggles the panel.
        private readonly PaperDevTools _devTools;
        /// <summary>Built-in DevTools (console / element inspector / profiler). Set
        /// <c>DevTools.Enabled = true</c> and press F12 to open.</summary>
        public PaperDevTools DevTools => _devTools;

        /// <summary>Append a message to the DevTools console.</summary>
        public void Log(string message, PaperDevTools.LogLevel level = PaperDevTools.LogLevel.Info) => _devTools.Log(message, level);

        // Events
        public Action? OnEndOfFramePreLayout = null;
        public Action? OnEndOfFramePostLayout = null;

        // Performance metrics
        public float MillisecondsSpent { get; private set; }
        public uint CountOfAllElements { get; private set; }

        // Public properties
        public Rect ScreenRect => new Rect(0, 0, _canvas?.Width ?? _width, _canvas?.Height ?? _height);

        /// <summary>Viewport width in logical units (matches <see cref="ScreenRect"/>.Width).</summary>
        public float Width => _canvas?.Width ?? _width;

        /// <summary>Viewport height in logical units (matches <see cref="ScreenRect"/>.Height).</summary>
        public float Height => _canvas?.Height ?? _height;

        public ElementHandle RootElement => _rootElementHandle;
        public Canvas Canvas => _canvas;
        public ICanvasRenderer Renderer => _renderer;

        /// <summary>
        /// Physical-pixels-per-logical-pixel ratio for the main viewport. Default is <c>(1,1)</c>.
        /// Set this to the host's DPI ratio (e.g. <c>(2,2)</c> on a Retina display) before
        /// <see cref="BeginFrame"/>. Paper uses this to scale vertex output and to rasterize fonts
        /// at the right density for crisp HiDPI rendering.
        /// </summary>
        public Float2 DisplayFramebufferScale = new Float2(1.0f, 1.0f);

        /// <summary>
        /// Accumulated scale applied to the registered default dimensional values by
        /// <see cref="ScaleAllSizes"/>.
        /// </summary>
        public float MainScale { get; private set; } = 1.0f;

        /// <summary>
        /// Convenience shorthand for <c>DisplayFramebufferScale.X</c>.
        /// </summary>
        public float DpiScale => DisplayFramebufferScale.X;

        /// <summary>
        /// Multiplies every registered default dimensional value by <paramref name="scaleFactor"/>.
        /// Call this <b>once</b> at init — typically with the monitor's DPI ratio — to adapt
        /// default style values (padding, border width, spacing, etc.) to a HiDPI display.
        /// </summary>
        public void ScaleAllSizes(float scaleFactor)
        {
            if (scaleFactor <= 0) throw new ArgumentOutOfRangeException(nameof(scaleFactor));
            MainScale *= scaleFactor;
            foreach (var scale in _scaledDefaults)
                scale(scaleFactor);
        }

        /// <summary>
        /// Registers a default value so <see cref="ScaleAllSizes"/> can scale it. The style
        /// subsystem calls this during init for each scalable default.
        /// </summary>
        internal void RegisterScaledDefault(Action<float> applyScale) => _scaledDefaults.Add(applyScale);

        private readonly List<Action<float>> _scaledDefaults = new List<Action<float>>();

        /// <summary>
        /// Gets the current parent element in the element hierarchy.
        /// </summary>
        public ElementHandle CurrentParent => _elementStack.Peek();

        #endregion

        #region Initialization and Frame Management

        /// <summary>
        /// Initializes Paper with a renderer and dimensions.
        /// </summary>
        /// <param name="renderer">The canvas renderer implementation</param>
        /// <param name="width">Viewport width</param>
        /// <param name="height">Viewport height</param>
        /// <param name="fontAtlas">Font atlas settings for text rendering</param>
        public Paper(ICanvasRenderer renderer, float width, float height, FontAtlasSettings fontAtlas)
        {
            _width = width;
            _height = height;
            _renderer = renderer;

            // Initialize element storage and create root element
            ClearElements();
            InitializeRootElement(_width, _height);
            _rootElementHandle = GetRootElementHandle();

            // Clear collections
            _elementStack.Clear();
            _IDStack.Clear();
            _createdElements.Clear();

            // Push the root element onto the stack
            _elementStack.Push(_rootElementHandle);

            // Create canvas
            _canvas = new Canvas(renderer, fontAtlas);

            InitializeInput();

            _devTools = new PaperDevTools(this);
        }

        /// <summary>
        /// Updates the viewport resolution.
        /// </summary>
        public void SetResolution(float width, float height)
        {
            _width = width;
            _height = height;
        }

        public void AddFallbackFont(FontFile font) => _canvas.AddFallbackFont(font);

        public IEnumerable<FontFile> EnumerateSystemFonts() => _canvas.EnumerateSystemFonts();

        public Float2 MeasureText(string text, float pixelSize, FontFile font, float letterSpacing = 0.0f) => _canvas.MeasureText(text, (float)pixelSize, font, (float)letterSpacing);

        public Float2 MeasureText(string text, TextLayoutSettings settings) => _canvas.MeasureText(text, settings);

        public TextLayout CreateLayout(string text, TextLayoutSettings settings) => _canvas.CreateLayout(text, settings);

        /// <summary>
        /// Begins a new UI frame, resetting the element hierarchy. Before calling this the host
        /// must have set <see cref="DisplayFramebufferScale"/> for the current frame
        /// (or supply <paramref name="dpiScale"/> here).
        /// </summary>
        /// <param name="deltaTime">Time elapsed since the last frame.</param>
        /// <param name="dpiScale">
        /// Optional convenience: when <c>&gt; 0</c>, sets <see cref="DisplayFramebufferScale"/> to
        /// <c>(dpiScale, dpiScale)</c> for this frame. Pass <c>&lt;= 0</c> to leave whatever the host
        /// already set on <see cref="DisplayFramebufferScale"/> untouched.
        /// </param>
        public void BeginFrame(float deltaTime, float dpiScale = 1.0f)
        {
            if (dpiScale > 0)
                DisplayFramebufferScale = new Float2(dpiScale, dpiScale);

            _timer.Restart();
            SetTime(deltaTime);

            _canvas.BeginFrame(_width, _height, DisplayFramebufferScale.X);

            _elementStack.Clear();

            ClearElements();
            InitializeRootElement(_width, _height);
            _rootElementHandle = GetRootElementHandle();

            // Initialize stacks
            _elementStack.Push(_rootElementHandle);
            _IDStack.Clear();
            _IDStack.Push(0);
            _createdElements.Clear();

            StartInputFrame();

            // Toggle / reserve space for the DevTools panel (no-op unless DevTools.Enabled).
            _devTools.OnBeginFrame();
        }

        /// <summary>
        /// Ends the current UI frame, performing layout calculations and rendering.
        /// </summary>
        public void EndFrame()
        {
            // Build the DevTools UI (if open) so it is part of this frame's tree, then time each phase.
            _devTools.OnEndFrameStart();
            long __t = _devTools.Timing ? Stopwatch.GetTimestamp() : 0;

            // Update element styles
            UpdateStyles(DeltaTime, RootElement);
            __t = _devTools.Phase("Styles", __t);

            // Layout phase
            OnEndOfFramePreLayout?.Invoke();
            ElementLayout.Layout(_rootElementHandle, this);
            OnEndOfFramePostLayout?.Invoke();
            __t = _devTools.Phase("Layout", __t);

            // Post-layout callbacks
            CallPostLayoutRecursive(RootElement);
            __t = _devTools.Phase("PostLayout", __t);

            // Compute per-element subtree culling bounds now that positions are final, so
            // RenderElement can skip subtrees that fall entirely outside the clip.
            ComputeCullingBounds(_rootElementHandle);
            __t = _devTools.Phase("Culling", __t);

            // Collect layered elements for independent hit testing
            _layeredElements.Clear();
            CollectLayeredElements(_rootElementHandle, Transform2D.Identity);
            __t = _devTools.Phase("Layered", __t);

            // Reset rendering state
            _canvas.ResetState();

            // Input and interaction handling
            HandleInteractions();
            __t = _devTools.Phase("Interaction", __t);

            // Render all elements.
            //
            // Deferred elements capture the canvas transform at collection time so they render
            // at the correct position even though they draw later. We bucket by layer value so
            // any int Layer works, not just the three named tiers; a child at layer 250 inside
            // a parent at layer 150 ends up in its own bucket and gets drained after layer 150.
            var deferred = _deferredRender;
            deferred.Clear();
            RenderElement(_rootElementHandle, Layer.Base, deferred);

            while (deferred.Count > 0)
            {
                // SortedDictionary keeps keys ascending, so the first is the lowest layer. foreach uses the
                // struct enumerator (no allocation), unlike LINQ .Keys.First().
                int nextLayer = 0;
                foreach (var k in deferred.Keys) { nextLayer = k; break; }
                var list = deferred[nextLayer];
                deferred.Remove(nextLayer);

                foreach (var (handle, transform) in list)
                {
                    _canvas.SaveState();
                    _canvas.CurrentTransform(transform);
                    RenderElement(handle, nextLayer, deferred);
                    _canvas.RestoreState();
                }

                list.Clear();
                _deferredBucketPool.Push(list);
            }
            __t = _devTools.Phase("Render", __t);

            // Update stats
            CountOfAllElements = (uint)_createdElements.Count;

            // Finalize rendering
            _canvas.Render();
            __t = _devTools.Phase("Upload", __t);

            EndInputFrame();

            // Cleanup
            EndOfFrameCleanupStyles(_createdElements);
            EndOfFrameCleanupStorage();

            // Performance measurement
            _timer.Stop();
            MillisecondsSpent = (float)_timer.Elapsed.TotalMilliseconds;

            // Capture snapshot + stats for next frame's DevTools panels.
            _devTools.OnEndFrameEnd();
        }

        /// <summary>
        /// Calls post-layout callbacks for an element and its children.
        /// </summary>
        private void CallPostLayoutRecursive(ElementHandle handle)
        {
            if (handle.IsValid == false) return;

            _elementStack.Push(handle);
            try
            {
                handle.Data.OnPostLayout?.Invoke(handle, new Rect(handle.Data.X, handle.Data.Y, handle.Data.X + handle.Data.LayoutWidth, handle.Data.Y + handle.Data.LayoutHeight));
                for (int i = 0; i < handle.Data.ChildIndices.Count; i++)
                {
                    var child = new ElementHandle(this, handle.Data.ChildIndices[i]);
                    CallPostLayoutRecursive(child);
                }
            }
            finally
            {
                _elementStack.Pop();
            }
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Renders an element and its children recursively with layering support. Elements whose
        /// <see cref="ElementData.Layer"/> is greater than <paramref name="currentLayer"/> get
        /// deferred into <paramref name="deferred"/> instead of rendering inline; the caller
        /// drains the dictionary in ascending key order so higher layers always render on top.
        /// </summary>
        private void RenderElement(in ElementHandle handle, int currentLayer, SortedDictionary<int, List<(ElementHandle handle, Transform2D transform)>>? deferred)
        {
            // Fast path when DevTools deep-profiling is off (the common case).
            if (!_devTools.DeepProfiling)
            {
                RenderElementInner(handle, currentLayer, deferred);
                return;
            }

            // Deep profile: the wall time of this call is the element's inclusive subtree render time
            // (children recurse through this same wrapper before it returns).
            int id = handle.Data.ID;
            int parentIndex = handle.Data.ParentIndex;
            int parentId = parentIndex >= 0 ? GetElementData(parentIndex).ID : 0;
            long start = Stopwatch.GetTimestamp();
            RenderElementInner(handle, currentLayer, deferred);
            _devTools.RecordRender(id, parentId, Stopwatch.GetTimestamp() - start);
        }

        private void RenderElementInner(in ElementHandle handle, int currentLayer, SortedDictionary<int, List<(ElementHandle handle, Transform2D transform)>>? deferred)
        {
            ref var data = ref handle.Data;

            if (data.Visible == false)
                return;

            if (data.Layer > currentLayer && deferred != null)
            {
                if (!deferred.TryGetValue(data.Layer, out var bucket))
                {
                    bucket = _deferredBucketPool.Count > 0 ? _deferredBucketPool.Pop() : new List<(ElementHandle handle, Transform2D transform)>();
                    deferred[data.Layer] = bucket;
                }
                bucket.Add((handle, _canvas.GetTransform()));
                return;
            }

            var rect = new Rect(data.X, data.Y, data.X + data.LayoutWidth, data.Y + data.LayoutHeight);
            _canvas.SaveState();

            // Apply element transform
            Transform2D styleTransform = data._elementStyle.GetTransformForElement(rect);
            _canvas.TransformBy(styleTransform);

            // Cull: if this whole subtree (including its shadow) lies outside the current clip and
            // nothing in it escapes to a higher layer, skip drawing it and all of its children.
            if (!data._cullHasLayerBreakout && _canvas.GetCurrentClipRect(out var clip) &&
                (data._cullMaxX < clip.Min.X || data._cullMinX > clip.Max.X ||
                 data._cullMaxY < clip.Min.Y || data._cullMinY > clip.Max.Y))
            {
                _canvas.RestoreState();
                return;
            }

            // Draw box shadow before background
            var rounded = (Float4)data._elementStyle.GetValue(GuiProp.Rounded);
            bool hasRounding = rounded.X > 0 || rounded.Y > 0 || rounded.Z > 0 || rounded.W > 0;
            var boxShadow = (BoxShadow)data._elementStyle.GetValue(GuiProp.BoxShadow);
            if (boxShadow.IsVisible)
            {
                // Soft falloff: the lit "core" of the brush box is the element box itself (no outward
                // buffer), and the feather (full falloff width) is twice the blur. Together with the
                // geometry buffer of one blur below, this keeps the OUTER radius at element+spread+blur
                // while pushing the solid core inward by a blur, so the shadow reads as a smooth gradient
                // instead of a near-solid shape with a thin fringe.
                float buffer = 0f;
                float sx = rect.Min.X + boxShadow.OffsetX - buffer - boxShadow.Spread;
                float sy = rect.Min.Y + boxShadow.OffsetY - buffer - boxShadow.Spread;
                float sw = rect.Size.X + (buffer * 2) + (boxShadow.Spread * 2);
                float sh = rect.Size.Y + (buffer * 2) + (boxShadow.Spread * 2);
                float radi = (float)(Maths.Max(Maths.Max(rounded.X, rounded.Y), Maths.Max(rounded.Z, rounded.W)));
                _canvas.SetBoxBrush(
                    sx + sw / 2,
                    sy + sh / 2,
                    sw,
                    sh,
                    radi,
                    (float)boxShadow.Blur * 2f,
                    boxShadow.Color,
                    Color32.FromArgb(0, boxShadow.Color));

                buffer = (boxShadow.Blur) * 1.0f;
                sx = rect.Min.X + boxShadow.OffsetX - buffer - boxShadow.Spread;
                sy = rect.Min.Y + boxShadow.OffsetY - buffer - boxShadow.Spread;
                sw = rect.Size.X + (buffer * 2) + (boxShadow.Spread * 2);
                sh = rect.Size.Y + (buffer * 2) + (boxShadow.Spread * 2);

                // Paint the halo as a ring: the shadow geometry with the element's own rounded rect
                // punched out (even-odd hole). Without this the shadow is painted under the element too,
                // which shows through any translucent fill (a glass window/card revealing its own shadow).
                _canvas.SaveState();
                _canvas.SetFillColor(Prowl.Vector.Color.White);
                _canvas.SetSolidity(WindingMode.OddEven);
                _canvas.BeginPath();
                AddRoundedContour(_canvas, sx, sy, sw, sh, rounded);
                AddRoundedContour(_canvas, (float)rect.Min.X, (float)rect.Min.Y, (float)rect.Size.X, (float)rect.Size.Y, rounded);
                _canvas.FillComplex();
                _canvas.RestoreState();

                _canvas.ClearBrush();
            }

            // Draw backdrop blur (frosted glass) behind the background. The blurred backdrop is laid
            // down with a transparent fill so the element's own background color acts as the glass tint.
            var backdropBlur = (float)data._elementStyle.GetValue(GuiProp.BackdropBlur);
            if (backdropBlur > 0f)
            {
                _canvas.SetBackdropBlur(backdropBlur);
                if (hasRounding)
                    _canvas.RoundedRectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, rounded.X, rounded.Y, rounded.Z, rounded.W, Prowl.Vector.Color.Transparent);
                else
                    _canvas.RectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, Prowl.Vector.Color.Transparent);
                _canvas.ClearBackdropBlur();
            }

            // Draw background (gradient overrides background color)
            var gradient = (Gradient)data._elementStyle.GetValue(GuiProp.BackgroundGradient);
            if (gradient.Type != GradientType.None)
            {
                switch (gradient.Type)
                {
                    case GradientType.Linear:
                        float lx1 = rect.Min.X + gradient.X1 * rect.Size.X;
                        float ly1 = rect.Min.Y + gradient.Y1 * rect.Size.Y;
                        float lx2 = rect.Min.X + gradient.X2 * rect.Size.X;
                        float ly2 = rect.Min.Y + gradient.Y2 * rect.Size.Y;
                        _canvas.SetLinearBrush(lx1, ly1, lx2, ly2, gradient.Color1, gradient.Color2);
                        break;
                    case GradientType.Radial:
                        float rcx = rect.Min.X + gradient.X1 * rect.Size.X;
                        float rcy = rect.Min.Y + gradient.Y1 * rect.Size.Y;
                        float ir = gradient.InnerRadius * Maths.Min(rect.Size.X, rect.Size.Y);
                        float or = gradient.OuterRadius * Maths.Min(rect.Size.X, rect.Size.Y);
                        _canvas.SetRadialBrush(rcx, rcy, ir, or, gradient.Color1, gradient.Color2);
                        break;
                    case GradientType.Box:
                        float bcx = rect.Min.X + gradient.X1 * rect.Size.X;
                        float bcy = rect.Min.Y + gradient.Y1 * rect.Size.Y;
                        float bw = gradient.Width * rect.Size.X;
                        float bh = gradient.Height * rect.Size.Y;
                        float brad = gradient.Radius * (float)Maths.Min(rect.Size.X, rect.Size.Y);
                        float bfeather = gradient.Feather * (float)Maths.Min(rect.Size.X, rect.Size.Y);
                        _canvas.SetBoxBrush(bcx, bcy, bw, bh, brad, bfeather, gradient.Color1, gradient.Color2);
                        break;
                }
                if (hasRounding)
                {
                    _canvas.RoundedRectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, rounded.X, rounded.Y, rounded.Z, rounded.W, Color.White);
                }
                else
                {
                    _canvas.RectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, Color.White);
                }
                _canvas.ClearBrush();
            }
            else
            {
                var backgroundColor = (Color)data._elementStyle.GetValue(GuiProp.BackgroundColor);
                if (backgroundColor.A > 0)
                {
                    if (hasRounding)
                        _canvas.RoundedRectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, rounded.X, rounded.Y, rounded.Z, rounded.W, backgroundColor);
                    else
                        _canvas.RectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, backgroundColor);
                }
            }

            // Draw background image if set (rendered on top of background color/gradient)
            var bgImage = data._elementStyle.GetValue(GuiProp.BackgroundImage);
            if (bgImage != null)
            {
                _canvas.DrawImage(bgImage, rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y);
            }

            // Draw border if needed
            var borderColor = (Color)data._elementStyle.GetValue(GuiProp.BorderColor);
            var borderWidth = (float)data._elementStyle.GetValue(GuiProp.BorderWidth);
            if (borderWidth > 0.0f && borderColor.A > 0)
            {
                _canvas.BeginPath();
                if(hasRounding)
                    _canvas.RoundedRect(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, rounded.X, rounded.Y, rounded.Z, rounded.W);
                else
                    _canvas.Rect(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y);
                _canvas.SetStrokeColor(borderColor);
                _canvas.SetStrokeWidth(borderWidth);
                _canvas.Stroke();
            }

            // Apply scissor if enabled
            if (data._scissorEnabled)
            {
                _canvas.IntersectScissor(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y);
            }

            // Draw text style
            if (!string.IsNullOrEmpty(data.Paragraph))
            {
                _canvas.SaveState();
                DrawText(handle, rect.Min.X, rect.Min.Y, (float)rect.Size.X, (float)rect.Size.Y);
                _canvas.RestoreState();
            }


            // Process custom render actions
            if (data._renderCommands != null)
            {
                foreach (var cmd in data._renderCommands)
                {
                    //_canvas.SaveState(); // Making this the Users responsibility to ensure they restore state, since not all custom draws will change state.
                    _elementStack.Push(handle);
                    try
                    {
                        cmd.RenderAction?.Invoke(_canvas, rect);
                    }
                    finally
                    {
                        _elementStack.Pop();
                        //_canvas.RestoreState();
                    }
                }
            }

            // Draw children
            foreach (var childIndex in data.ChildIndices)
            {
                var child = new ElementHandle(this, childIndex);
                RenderElement(child, currentLayer, deferred);
            }

            // Process foreground render actions (after children)
            if (data._foregroundRenderCommands != null)
            {
                foreach (var cmd in data._foregroundRenderCommands)
                {
                    _elementStack.Push(handle);
                    try
                    {
                        cmd.RenderAction?.Invoke(_canvas, rect);
                    }
                    finally
                    {
                        _elementStack.Pop();
                    }
                }
            }

            _canvas.RestoreState();
        }

        /// <summary>
        /// Computes, for <paramref name="handle"/> and every descendant, the axis-aligned bounds of
        /// its whole subtree (in the element's own local space, grown to include box shadow) and
        /// whether the subtree contains a higher-layer element that escapes clipping. Results are
        /// stored on each element for <see cref="RenderElement"/> to cull against; returns this
        /// element's subtree bounds so the parent can fold them in under the child's transform.
        /// </summary>
        private Rect ComputeCullingBounds(ElementHandle handle)
        {
            ref var data = ref handle.Data;

            // Own visual bounds: the layout rect grown by the border and box shadow (glow) reach.
            var rect = data.LayoutRect;
            float minX = rect.Min.X, minY = rect.Min.Y, maxX = rect.Max.X, maxY = rect.Max.Y;

            float border = (float)data._elementStyle.GetValue(GuiProp.BorderWidth);
            if (border > 0f)
            {
                minX -= border; minY -= border; maxX += border; maxY += border;
            }

            var shadow = (BoxShadow)data._elementStyle.GetValue(GuiProp.BoxShadow);
            if (shadow.IsVisible)
            {
                float reach = shadow.Spread + shadow.Blur;
                minX = Maths.Min(minX, rect.Min.X + shadow.OffsetX - reach);
                minY = Maths.Min(minY, rect.Min.Y + shadow.OffsetY - reach);
                maxX = Maths.Max(maxX, rect.Max.X + shadow.OffsetX + reach);
                maxY = Maths.Max(maxY, rect.Max.Y + shadow.OffsetY + reach);
            }

            bool breakout = false;

            for (int i = 0; i < data.ChildIndices.Count; i++)
            {
                var child = new ElementHandle(this, data.ChildIndices[i]);
                Rect cb = ComputeCullingBounds(child);

                ref var childData = ref child.Data;

                // Fold the child in under its own style transform, matching what RenderElement applies.
                Transform2D xform = childData._elementStyle.GetTransformForElement(childData.LayoutRect);
                cb = TransformBoundsAABB(xform, cb);

                minX = Maths.Min(minX, cb.Min.X);
                minY = Maths.Min(minY, cb.Min.Y);
                maxX = Maths.Max(maxX, cb.Max.X);
                maxY = Maths.Max(maxY, cb.Max.Y);

                breakout = breakout || childData._cullHasLayerBreakout || childData.Layer > data.Layer;
            }

            data._cullMinX = minX; data._cullMinY = minY;
            data._cullMaxX = maxX; data._cullMaxY = maxY;
            data._cullHasLayerBreakout = breakout;

            return new Rect(minX, minY, maxX, maxY);
        }

        // Axis-aligned bounds of a rectangle after mapping its corners through 'xform'.
        private static Rect TransformBoundsAABB(Transform2D xform, Rect r)
        {
            var p0 = xform.TransformPoint(new Float2(r.Min.X, r.Min.Y));
            var p1 = xform.TransformPoint(new Float2(r.Max.X, r.Min.Y));
            var p2 = xform.TransformPoint(new Float2(r.Max.X, r.Max.Y));
            var p3 = xform.TransformPoint(new Float2(r.Min.X, r.Max.Y));
            float lx = Maths.Min(Maths.Min(p0.X, p1.X), Maths.Min(p2.X, p3.X));
            float ly = Maths.Min(Maths.Min(p0.Y, p1.Y), Maths.Min(p2.Y, p3.Y));
            float hx = Maths.Max(Maths.Max(p0.X, p1.X), Maths.Max(p2.X, p3.X));
            float hy = Maths.Max(Maths.Max(p0.Y, p1.Y), Maths.Max(p2.Y, p3.Y));
            return new Rect(lx, ly, hx, hy);
        }

        /// <summary>
        /// Appends a closed rounded-rect contour to the canvas' current path (unlike
        /// <see cref="Canvas.RoundedRect"/>, it does not call BeginPath), so several contours can be
        /// combined in one path — e.g. an outer rect plus an inner hole for an even-odd fill.
        /// </summary>
        private static void AddRoundedContour(Canvas canvas, float x, float y, float w, float h, Float4 r)
        {
            if (w <= 0 || h <= 0) return;
            float mr = MathF.Min(w, h) * 0.5f;
            float tl = MathF.Min((float)r.X, mr), tr = MathF.Min((float)r.Y, mr);
            float br = MathF.Min((float)r.Z, mr), bl = MathF.Min((float)r.W, mr);
            float halfPi = MathF.PI * 0.5f;
            canvas.MoveTo(x + tl, y);
            canvas.LineTo(x + w - tr, y);
            canvas.Arc(x + w - tr, y + tr, tr, -halfPi, 0f, false);
            canvas.LineTo(x + w, y + h - br);
            canvas.Arc(x + w - br, y + h - br, br, 0f, halfPi, false);
            canvas.LineTo(x + bl, y + h);
            canvas.Arc(x + bl, y + h - bl, bl, halfPi, MathF.PI, false);
            canvas.LineTo(x, y + tl);
            canvas.Arc(x + tl, y + tl, tl, MathF.PI, MathF.PI + halfPi, false);
            canvas.ClosePath();
        }

        private void DrawText(in ElementHandle handle, float x, float y, float availableWidth, float availableHeight)
        {
            if (string.IsNullOrWhiteSpace(handle.Data.Paragraph)) return;

            //if (!handle.Data.ProcessedText)
                handle.Data.ProcessText(this, availableWidth);

            Canvas canvas = handle.Owner?.Canvas ?? throw new InvalidOperationException("Owner paper or canvas is not set.");

            var color = (Color32)(Color)handle.Data._elementStyle.GetValue(GuiProp.TextColor);

            // Calculate vertical alignment offset
            float yOffset = 0;
            Float2 textSize;

            // TextLayout.Size is in pixel space (because Canvas.CreateLayout scales settings by
            // FramebufferScale for crispness). Convert back to logical units for alignment math.
            float invScale = 1.0f / canvas.FramebufferScale;

            if (handle.Data.IsRichText)
            {
                if (handle.Data._quillRichText == null) throw new InvalidOperationException("Rich text layout is not processed.");
                textSize = handle.Data._quillRichText.Value.Size; // already in logical units
            }
            else if (handle.Data.IsMarkdown == false)
            {
                if (handle.Data._textLayout == null) throw new InvalidOperationException("Text layout is not processed.");

                textSize = (Float2)handle.Data._textLayout.Size * invScale;
            }
            else
            {
                if (handle.Data._quillMarkdown == null) throw new InvalidOperationException("Markdown layout is not processed.");

                var markdownResult = handle.Data._quillMarkdown;
                textSize = (markdownResult?.Size ?? Float2.Zero) * invScale;
            }

            // Apply vertical alignment based on TextAlignment
            switch (handle.Data.TextAlignment)
            {
                case TextAlignment.MiddleLeft:
                case TextAlignment.MiddleCenter:
                case TextAlignment.MiddleRight:
                    yOffset = (availableHeight - textSize.Y) / 2.0f;
                    break;

                case TextAlignment.BottomLeft:
                case TextAlignment.BottomCenter:
                case TextAlignment.BottomRight:
                    yOffset = availableHeight - textSize.Y;
                    break;

                case TextAlignment.Left:
                case TextAlignment.Center:
                case TextAlignment.Right:
                default:
                    yOffset = 0; // Top alignment (default)
                    break;
            }

            // Apply the calculated offset to the y position
            float finalY = y + yOffset;

            if (handle.Data.IsRichText)
            {
                var rt = handle.Data._quillRichText.Value;
                canvas.DrawRichText(rt, new Float2(x, finalY), Time);
            }
            else if (handle.Data.IsMarkdown == false)
            {
                canvas.DrawLayout(handle.Data._textLayout, x, finalY, color);
            }
            else
            {
                var markdownResult = handle.Data._quillMarkdown;
                canvas.DrawMarkdown(markdownResult ?? new Canvas.QuillMarkdown(), new Float2(x, finalY));
            }
        }

        #endregion

        #region Element Management

        /// <summary>
        /// Finds an element by its unique ID.
        /// </summary>
        /// <param name="id">The ID to search for</param>
        /// <returns>The found element or null if not found</returns>
        public ElementHandle FindElementByID(int id)
        {
            var handle = FindElementHandleByID(id);
            return handle;
        }

        /// <summary>
        /// Creates a generic layout container.
        /// </summary>
        /// <param name="stringID">String identifier for the element</param>
        /// <param name="intID">Integer identifier useful for when creating elements in loops</param>
        /// <param name="lineID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the element</returns>
        public ElementBuilder Box(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
        {
            if (stringID == null)
            {
                throw new ArgumentException(nameof(stringID));
            }

            int storageHash = HashCode.Combine(CurrentParent.Data.ID, _IDStack.Peek(), stringID, intID, lineID);

            if (!_createdElements.Add(storageHash))
                throw new Exception($"Element already exists with this ID: {stringID}:{intID}:{lineID} = {storageHash} Parent: {CurrentParent.Data.ID}\nPlease use a different ID.");

            var handle = CreateElement(storageHash);
            var builder = new ElementBuilder(this, handle);

            AddChild(ref handle);

            return builder;
        }

        /// <summary>
        /// Creates a row layout container (horizontal layout).
        /// </summary>
        public ElementBuilder Row(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
            => Box(stringID, intID, lineID).LayoutType(LayoutType.Row);

        /// <summary>
        /// Creates a column layout container (vertical layout).
        /// </summary>
        public ElementBuilder Column(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
            => Box(stringID, intID, lineID).LayoutType(LayoutType.Column);

        /// <summary>
        /// Moves the current parent element to the root of the hierarchy.
        /// Useful for things like popups or modals that need to be rendered at the top level.
        /// You can combine this with Depth to ensure something always renders ontop!
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void MoveToRoot()
        {
            if (CurrentParent.IsValid == false)
                throw new Exception("Not currently inside an Element.");

            var parentHandle = CurrentParent.GetParentHandle();
            if (parentHandle.IsValid)
                parentHandle.Data.ChildIndices.Remove(CurrentParent.Index);

            RootElement.Data.ChildIndices.Add(CurrentParent.Index);
        }

        /// <summary>
        /// Adds a child element to the current parent.
        /// </summary>
        internal void AddChild(ref ElementHandle handle)
        {
            var parentHandle = handle.GetParentHandle();

            if (parentHandle.IsValid)
                throw new Exception("Element already has a parent.");

            handle.Data.ParentIndex = CurrentParent.Index;
            CurrentParent.Data.ChildIndices.Add(handle.Index);
        }

        public void Draw(Action<Canvas, Rect> renderAction)
        {
            var current = CurrentParent;
            Draw(ref current, renderAction);
        }

        /// <summary>
        /// Adds a custom render action to an element. Draws before children.
        /// </summary>
        public void Draw(ref ElementHandle handle, Action<Canvas, Rect> renderAction)
        {
            if (renderAction == null)
                throw new ArgumentException(nameof(renderAction));

            handle.Data._renderCommands ??= new List<ElementRenderCommand>();
            handle.Data._renderCommands.Add(new ElementRenderCommand {
                Element = handle,
                RenderAction = renderAction,
            });
        }

        /// <summary>
        /// Adds a foreground render action to the current parent. Draws after children.
        /// </summary>
        public void DrawForeground(Action<Canvas, Rect> renderAction)
        {
            var current = CurrentParent;
            DrawForeground(ref current, renderAction);
        }

        /// <summary>
        /// Adds a custom render action that draws after all children have been rendered.
        /// Useful for overlays, borders, and decorations that should appear on top of content.
        /// </summary>
        public void DrawForeground(ref ElementHandle handle, Action<Canvas, Rect> renderAction)
        {
            if (renderAction == null)
                throw new ArgumentException(nameof(renderAction));

            handle.Data._foregroundRenderCommands ??= new List<ElementRenderCommand>();
            handle.Data._foregroundRenderCommands.Add(new ElementRenderCommand {
                Element = handle,
                RenderAction = renderAction,
            });
        }

        #endregion

        #region ID Stack Management

        /// <summary>
        /// Pushes an ID onto the ID stack to create a new scope.
        /// </summary>
        public void PushID(string id)
        {
            PushID(id.GetHashCode());
        }

        /// <summary>
        /// Pushes an ID onto the ID stack to create a new scope.
        /// </summary>
        public void PushID(int id)
        {
            _IDStack.Push(HashCode.Combine(id, _IDStack.Peek()));
        }

        /// <summary>
        /// Pops the current ID from the stack, returning to the parent scope.
        /// </summary>
        public void PopID()
        {
            if (_IDStack.Count > 1)
                _IDStack.Pop();
            else
                throw new Exception("Cannot pop the root ID.");
        }

        #endregion

        #region Element Storage

        /// <summary> Get a value from the global GUI storage this persists across all Frames and Elements </summary>
        public T GetRootStorage<T>(string key) => GetElementStorage<T>(_rootElementHandle, key, default);
        /// <summary> Set a value in the root element </summary>
        public void SetRootStorage<T>(string key, T value) => SetElementStorage(_rootElementHandle, key, value);

        /// <summary> Get a value from the current element's storage </summary>
        public T GetElementStorage<T>(string key, T defaultValue = default) => GetElementStorage(CurrentParent, key, defaultValue);

        /// <summary> Get a value from the current element's storage </summary>
        public T GetElementStorage<T>(ElementHandle el, string key, T defaultValue = default)
        {
            if (!_storage.TryGetValue(el.Data.ID, out var storage))
                return defaultValue;

            if (storage.ContainsKey(key))
                return (T)storage[key]!;

            return defaultValue;
        }

        /// <summary> Check if a key exists in the current element's storage </summary>
        public bool HasElementStorage(ElementHandle el, string key) => _storage.TryGetValue(el.Data.ID, out var storage) && storage.ContainsKey(key);

        /// <summary> Set a value in the current element's storage </summary>
        public void SetElementStorage<T>(string key, T value) => SetElementStorage(CurrentParent, key, value);
        /// <summary> Set a value in the current element's storage </summary>
        public void SetElementStorage<T>(ElementHandle el, string key, T value)
        {
            if (!_storage.TryGetValue(el.Data.ID, out var storage))
                _storage[el.Data.ID] = storage = new Hashtable();

            storage[key] = value;
        }

        internal T GetElementStorageById<T>(int id, string key, T defaultValue = default)
        {
            if (!_storage.TryGetValue(id, out var storage)) return defaultValue;
            if (storage.ContainsKey(key)) return (T)storage[key]!;
            return defaultValue;
        }

        internal void SetElementStorageById<T>(int id, string key, T value)
        {
            if (!_storage.TryGetValue(id, out var storage))
                _storage[id] = storage = new Hashtable();
            storage[key] = value;
        }

        internal void ClearElementStorageKey(int id, string key)
        {
            if (_storage.TryGetValue(id, out var storage))
                storage.Remove(key);
        }

        /// <summary>DevTools helper: the raw per-element storage table for an ID (null if none).</summary>
        internal Hashtable DebugGetStorage(int id) => _storage.TryGetValue(id, out var storage) ? storage : null;

        /// <summary>
        /// Resets animation start time on a rich-text element — replays typewriter / shake / etc.
        /// from the next frame's draw. No-op if the element has no rich-text layout cached yet.
        /// </summary>
        public void ResetRichText(ElementHandle el)
        {
            ClearElementStorageKey(el.Data.ID, RichTextLayoutKey);
            ClearElementStorageKey(el.Data.ID, RichTextSourceKey);
            ClearElementStorageKey(el.Data.ID, RichTextWidthKey);
        }

        // Storage keys for the per-element rich-text layout cache.
        internal const string RichTextLayoutKey = "_pp_rt_layout";
        internal const string RichTextSourceKey = "_pp_rt_source";
        internal const string RichTextWidthKey = "_pp_rt_width";

        // Storage keys for the per-element plain-text layout cache (width-independent text only).
        internal const string PlainTextLayoutKey = "_pp_pt_layout";
        internal const string PlainTextKeyKey = "_pp_pt_key";

        // Storage keys for the per-element truncated-text layout cache (keyed on source text + width).
        internal const string TruncTextLayoutKey = "_pp_tr_layout";
        internal const string TruncTextKeyKey = "_pp_tr_key";

        private void EndOfFrameCleanupStorage()
        {
            // Collect stale ids into a reused scratch list (can't Remove while enumerating _storage.Keys),
            // avoiding the per-frame _storage.Keys.ToArray() allocation.
            _storageCleanupScratch.Clear();
            foreach (var storedID in _storage.Keys)
            {
                // We didnt create this element this frame, so it no longer exists, delete any storage for it.
                if (!_createdElements.Contains(storedID))
                    _storageCleanupScratch.Add(storedID);
            }

            for (int i = 0; i < _storageCleanupScratch.Count; i++)
                _storage.Remove(_storageCleanupScratch[i]);
        }

        #endregion

        #region Layout Helpers

        /// <summary>
        /// Creates a stretch unit value with the specified factor.
        /// </summary>
        public UnitValue Stretch(float factor = 1f) => UnitValue.Stretch(factor);

        /// <summary>
        /// Creates a pixel-based unit value.
        /// </summary>
        public UnitValue Pixels(float value) => UnitValue.Pixels(value);

        /// <summary>
        /// Creates a percentage-based unit value with optional pixel offset.
        /// </summary>
        public UnitValue Percent(float value, float pixelOffset = 0f) => UnitValue.Percentage(value, pixelOffset);

        /// <summary>
        /// Creates an auto-sized unit value.
        /// </summary>
        public UnitValue Auto => UnitValue.Auto;

        #endregion
    }
}
