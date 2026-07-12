using System;
using System.Collections.Generic;
using System.Linq;
using Prowl.Scribe;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Utilities;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Defines all available style properties for UI elements.
    /// </summary>
    public enum GuiProp
    {
        #region Visual Properties
        BackgroundColor,
        BackgroundGradient,
        BorderColor,
        BorderWidth,
        Rounded,
        BoxShadow,
        BackdropBlur,
        #endregion

        #region Layout Properties
        // Core sizing
        AspectRatio,
        Width,
        Height,
        MinWidth,
        MaxWidth,
        MinHeight,
        MaxHeight,

        // Positioning
        Left,
        Right,
        Top,
        Bottom,
        MinLeft,
        MaxLeft,
        MinRight,
        MaxRight,
        MinTop,
        MaxTop,
        MinBottom,
        MaxBottom,

        // Child layout
        ChildLeft,
        ChildRight,
        ChildTop,
        ChildBottom,

        // Spacing
        RowBetween,
        ColBetween,

        // Padding (parent-side inset; pure layout, no visual)
        PaddingLeft,
        PaddingRight,
        PaddingTop,
        PaddingBottom,
        #endregion

        #region Transform Properties
        TranslateX,
        TranslateY,
        ScaleX,
        ScaleY,
        Rotate,
        OriginX,
        OriginY,
        SkewX,
        SkewY,
        Transform,
        #endregion

        #region Image Properties
        BackgroundImage,
        #endregion

        #region Text Properties
        TextColor,

        WordSpacing,
        LetterSpacing,
        LineHeight,

        TabSize,
        FontSize,
        TextQuality,
        #endregion
    }

    /// <summary>
    /// Builds transformation matrices for UI elements.
    /// </summary>
    public class TransformBuilder
    {
        #region Fields

        private float _translateX = 0;
        private float _translateY = 0;
        private float _scaleX = 1;
        private float _scaleY = 1;
        private float _rotate = 0;
        private float _skewX = 0;
        private float _skewY = 0;
        private float _originX = 0.5f; // Default to center (50%)
        private float _originY = 0.5f; // Default to center (50%)
        private Transform2D? _customTransform = null;

        #endregion

        #region Builder Methods

        /// <summary>
        /// Sets the X translation.
        /// </summary>
        public TransformBuilder SetTranslateX(float x)
        {
            _translateX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y translation.
        /// </summary>
        public TransformBuilder SetTranslateY(float y)
        {
            _translateY = y;
            return this;
        }

        /// <summary>
        /// Sets the X scale factor.
        /// </summary>
        public TransformBuilder SetScaleX(float x)
        {
            _scaleX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y scale factor.
        /// </summary>
        public TransformBuilder SetScaleY(float y)
        {
            _scaleY = y;
            return this;
        }

        /// <summary>
        /// Sets the rotation angle in degrees.
        /// </summary>
        public TransformBuilder SetRotate(float angleInDegrees)
        {
            _rotate = angleInDegrees;
            return this;
        }

        /// <summary>
        /// Sets the X skew angle.
        /// </summary>
        public TransformBuilder SetSkewX(float angle)
        {
            _skewX = angle;
            return this;
        }

        /// <summary>
        /// Sets the Y skew angle.
        /// </summary>
        public TransformBuilder SetSkewY(float angle)
        {
            _skewY = angle;
            return this;
        }

        /// <summary>
        /// Sets the X origin point (0-1 range).
        /// </summary>
        public TransformBuilder SetOriginX(float x)
        {
            _originX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y origin point (0-1 range).
        /// </summary>
        public TransformBuilder SetOriginY(float y)
        {
            _originY = y;
            return this;
        }

        /// <summary>
        /// Sets a custom transform to be applied.
        /// </summary>
        public TransformBuilder SetCustomTransform(Transform2D transform)
        {
            _customTransform = transform;
            return this;
        }

        /// <summary>Resets all properties to their defaults so a single instance can be reused.</summary>
        public void Reset()
        {
            _translateX = 0; _translateY = 0;
            _scaleX = 1; _scaleY = 1;
            _rotate = 0; _skewX = 0; _skewY = 0;
            _originX = 0.5f; _originY = 0.5f;
            _customTransform = null;
        }

        #endregion

        #region Build Method

        /// <summary>
        /// Builds the final transform matrix following the order: translate, rotate, scale, skew.
        /// </summary>
        /// <param name="rect">The rectangle to transform.</param>
        /// <returns>The complete transformation matrix.</returns>
        public Transform2D Build(Rect rect)
        {
            // Calculate origin in actual pixels
            float originX = rect.Min.X + _originX * rect.Size.X;
            float originY = rect.Min.Y + _originY * rect.Size.Y;

            // Create transformation matrix
            Transform2D result = Transform2D.Identity;

            // Create a matrix that transforms from origin
            Transform2D originMatrix = Transform2D.CreateTranslation(-originX, -originY);

            // Apply transforms in order: translate, rotate, scale, skew
            Transform2D transformMatrix = Transform2D.Identity;

            // 1. Translate
            if (_translateX != 0 || _translateY != 0)
                transformMatrix *= Transform2D.CreateTranslation(_translateX, _translateY);

            // 2. Rotate
            if (_rotate != 0)
                transformMatrix *= Transform2D.CreateRotation(_rotate);

            // 3. Scale
            if (_scaleX != 1 || _scaleY != 1)
                transformMatrix *= Transform2D.CreateScale(_scaleX, _scaleY);

            // 4. Skew
            if (_skewX != 0)
                transformMatrix *= Transform2D.CreateSkewX(_skewX);
            if (_skewY != 0)
                transformMatrix *= Transform2D.CreateSkewY(_skewY);

            // 5. Apply custom transform if specified
            if (_customTransform.HasValue)
                transformMatrix *= _customTransform.Value;

            // Complete transformation: move to origin, apply transform, move back from origin
            result = Transform2D.CreateTranslation(originX, originY)
                     * transformMatrix
                     * Transform2D.CreateTranslation(-originX, -originY);

            return result;
        }

        #endregion
    }

    /// <summary>
    /// Configuration for property transitions.
    /// </summary>
    public class TransitionConfig
    {
        /// <summary>Duration of the transition in seconds.</summary>
        public float Duration { get; set; }

        /// <summary>Optional easing function to control transition timing.</summary>
        public Func<float, float>? EasingFunction { get; set; }
    }

    /// <summary>
    /// Manages styling and transitions for UI elements.
    /// </summary>
    internal class ElementStyle
    {
        #region Fields

        // Resolved current values as typed fields (see StyleValues) - what layout/render read every
        // frame. Reset to the defaults at the start of each frame (BeginFrame); the builder then
        // re-declares this frame's values, so anything not re-declared naturally reverts to default.
        private StyleValues _current = _defaultStyleValues;

        // Lazily allocated, and only for elements that actually configure a transition. Non-animated
        // elements carry none of the transition machinery.
        private Transitions? _transitions;

        // Inheritance (opt-in via InheritStyle; usually null).
        private ElementStyle? _parent;

        // The default value for every property, as a single StyleValues (mask left at 0 = "unset").
        // BeginFrame copies this into _current to revert an element to defaults each frame.
        private static readonly StyleValues _defaultStyleValues = CreateDefaults();

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a fresh frame for this element: reverts current values to their defaults (unset), so
        /// the builder's declarations this frame define the element and anything omitted reverts. Fields
        /// hold defaults again; persistent animation state lives in <see cref="_transitions"/>.
        /// </summary>
        public void BeginFrame()
        {
            _current = _defaultStyleValues;
            _transitions?.BeginFrame();
        }

        /// <summary>
        /// Sets the parent style for inheritance.
        /// </summary>
        public void SetParent(ElementStyle? currentStyle)
        {
            _parent = currentStyle;
        }

        /// <summary>
        /// Checks if a property has a value.
        /// </summary>
        public bool HasValue(GuiProp property) => _current.Has(property);

        /// <summary>True if the property is currently mid-transition (used by DevTools).</summary>
        internal bool IsAnimating(GuiProp property) => _transitions != null && _transitions.IsAnimating(property);

        /// <summary>
        /// Gets the current value of a property, falling back to parent or default.
        /// </summary>
        public object GetValue(GuiProp property)
        {
            // Boxing shim for cold callers (DevTools, templates); hot readers use the typed accessors.
            // Inherit from the parent only when not set locally; otherwise GetBoxed returns the set
            // value, or the field's default when unset.
            if (_parent != null && !_current.Has(property))
                return _parent.GetValue(property);
            return _current.GetBoxed(property);
        }

        // ── Typed accessors (no boxing / no dictionary) for the hot layout & render loops ──
        // The common case (no inherited parent) is a direct typed field read.

        /// <summary>Typed read of a UnitValue property (layout), honouring set/parent/default.</summary>
        public UnitValue GetUnit(GuiProp property)
        {
            if (_parent != null && !_current.Has(property))
                return (UnitValue)_parent.GetValue(property);
            return _current.GetUnit(property);
        }

        public Color GetBackgroundColor() => (_parent != null && !_current.Has(GuiProp.BackgroundColor)) ? (Color)_parent.GetValue(GuiProp.BackgroundColor) : _current.BackgroundColor;
        public Gradient GetBackgroundGradient() => (_parent != null && !_current.Has(GuiProp.BackgroundGradient)) ? (Gradient)_parent.GetValue(GuiProp.BackgroundGradient) : _current.BackgroundGradient;
        public Color GetBorderColor() => (_parent != null && !_current.Has(GuiProp.BorderColor)) ? (Color)_parent.GetValue(GuiProp.BorderColor) : _current.BorderColor;
        public float GetBorderWidth() => (_parent != null && !_current.Has(GuiProp.BorderWidth)) ? (float)_parent.GetValue(GuiProp.BorderWidth) : _current.BorderWidth;
        public Float4 GetRounded() => (_parent != null && !_current.Has(GuiProp.Rounded)) ? (Float4)_parent.GetValue(GuiProp.Rounded) : _current.Rounded;
        public BoxShadow GetBoxShadow() => (_parent != null && !_current.Has(GuiProp.BoxShadow)) ? (BoxShadow)_parent.GetValue(GuiProp.BoxShadow) : _current.BoxShadow;
        public float GetBackdropBlur() => (_parent != null && !_current.Has(GuiProp.BackdropBlur)) ? (float)_parent.GetValue(GuiProp.BackdropBlur) : _current.BackdropBlur;
        public object GetBackgroundImage() => (_parent != null && !_current.Has(GuiProp.BackgroundImage)) ? _parent.GetValue(GuiProp.BackgroundImage) : _current.BackgroundImage;
        public Color GetTextColor() => (_parent != null && !_current.Has(GuiProp.TextColor)) ? (Color)_parent.GetValue(GuiProp.TextColor) : _current.TextColor;
        public float GetAspectRatio() => (_parent != null && !_current.Has(GuiProp.AspectRatio)) ? (float)_parent.GetValue(GuiProp.AspectRatio) : _current.AspectRatio;
        public float GetFontSize() => (_parent != null && !_current.Has(GuiProp.FontSize)) ? (float)_parent.GetValue(GuiProp.FontSize) : _current.FontSize;
        public float GetLineHeight() => (_parent != null && !_current.Has(GuiProp.LineHeight)) ? (float)_parent.GetValue(GuiProp.LineHeight) : _current.LineHeight;
        public float GetLetterSpacing() => (_parent != null && !_current.Has(GuiProp.LetterSpacing)) ? (float)_parent.GetValue(GuiProp.LetterSpacing) : _current.LetterSpacing;
        public float GetWordSpacing() => (_parent != null && !_current.Has(GuiProp.WordSpacing)) ? (float)_parent.GetValue(GuiProp.WordSpacing) : _current.WordSpacing;
        public int GetTabSize() => (_parent != null && !_current.Has(GuiProp.TabSize)) ? (int)_parent.GetValue(GuiProp.TabSize) : _current.TabSize;
        public FontQuality GetTextQuality() => (_parent != null && !_current.Has(GuiProp.TextQuality)) ? (FontQuality)_parent.GetValue(GuiProp.TextQuality) : _current.TextQuality;

        /// <summary>
        /// Sets a property value directly (already-resolved values such as the root size). In the new
        /// model this is the same as declaring a value for the frame.
        /// </summary>
        public void SetDirectValue(GuiProp property, object value) => _current.Set(property, value);

        /// <summary>
        /// Declares a property's value for this frame. Applied straight to the current values; if the
        /// property is animating, the transition pass (Update) overrides it with the tweened value.
        /// </summary>
        public void SetNextValue(GuiProp property, object value) => _current.Set(property, value);

        /// <summary>
        /// Configures a transition for a property this frame (re-declared each frame, as before).
        /// </summary>
        public void SetTransitionConfig(GuiProp property, float duration, Func<float, float>? easing = null)
            => (_transitions ??= new Transitions()).Configure(property, duration, easing);

        /// <summary>
        /// Advances any per-frame transitions, overriding the declared values in <see cref="_current"/>
        /// with their tweened values. A no-op (and free) for elements that configured no transitions.
        /// </summary>
        public void Update(float deltaTime)
        {
            _transitions?.Advance(deltaTime, ref _current, this);
        }

        /// <summary>
        /// Gets the complete transform for an element.
        /// </summary>
        // Reused per thread so the per-element per-frame transform build (render + every hit-test walk)
        // doesn't allocate a TransformBuilder each call. Non-reentrant: Build completes before we recurse.
        [ThreadStatic] private static TransformBuilder? s_transformBuilder;

        public Transform2D GetTransformForElement(Rect rect)
        {
            TransformBuilder builder = s_transformBuilder ??= new TransformBuilder();
            builder.Reset();

            // Set transform properties from the current values (typed, only when explicitly set).
            if (_current.Has(GuiProp.TranslateX)) builder.SetTranslateX(_current.TranslateX);
            if (_current.Has(GuiProp.TranslateY)) builder.SetTranslateY(_current.TranslateY);
            if (_current.Has(GuiProp.ScaleX)) builder.SetScaleX(_current.ScaleX);
            if (_current.Has(GuiProp.ScaleY)) builder.SetScaleY(_current.ScaleY);
            if (_current.Has(GuiProp.Rotate)) builder.SetRotate(_current.Rotate);
            if (_current.Has(GuiProp.SkewX)) builder.SetSkewX(_current.SkewX);
            if (_current.Has(GuiProp.SkewY)) builder.SetSkewY(_current.SkewY);
            if (_current.Has(GuiProp.OriginX)) builder.SetOriginX(_current.OriginX);
            if (_current.Has(GuiProp.OriginY)) builder.SetOriginY(_current.OriginY);
            if (_current.Has(GuiProp.Transform)) builder.SetCustomTransform(_current.Transform);

            return builder.Build(rect);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Interpolates between two values based on their type.
        /// </summary>
        internal object Interpolate(object start, object end, float t)
        {
            if (start is float floatStart && end is float floatEnd)
            {
                return floatStart + (floatEnd - floatStart) * t;
            }
            else if(start is double doubleStart && end is double doubleEnd)
            {
                return doubleStart + (doubleEnd - doubleStart) * t;
            }
            else if (start is int intStart && end is int intEnd)
            {
                return intStart + (int)((intEnd - intStart) * t);
            }
            else if (start is Prowl.Vector.Color colorStart && end is Prowl.Vector.Color colorEnd)
            {
                return InterpolateColor(colorStart, colorEnd, t);
            }
            else if (start is Float2 vectorStart && end is Float2 vectorEnd)
            {
                return Maths.Lerp(vectorStart, vectorEnd, t);
            }
            else if (start is Float3 vector3Start && end is Float3 vector3End)
            {
                return Maths.Lerp(vector3Start, vector3End, t);
            }
            else if (start is Float4 vector4Start && end is Float4 vector4End)
            {
                return Maths.Lerp(vector4Start, vector4End, t);
            }
            else if (start is UnitValue unitStart && end is UnitValue unitEnd)
            {
                return UnitValue.Lerp(unitStart, unitEnd, t);
            }
            else if (start is Transform2D transformStart && end is Transform2D transformEnd)
            {
                return Transform2D.Lerp(transformStart, transformEnd, t);
            }
            else if (start is string startString && end is string endString)
            {
                return t > 0.5 ? endString : startString;
            }
            else if (start is Gradient gradientStart && end is Gradient gradientEnd)
            {
                return Gradient.Lerp(gradientStart, gradientEnd, t);
            }
            else if (start is BoxShadow shadowStart && end is BoxShadow shadowEnd)
            {
                return BoxShadow.Lerp(shadowStart, shadowEnd, t);
            }

            // Default to just returning the end value
            return end;
        }

        /// <summary>
        /// Interpolates between two colors.
        /// </summary>
        private Color InterpolateColor(Color start, Color end, float t)
        {
            // If start is fully transparent, replace its RGB with end's RGB
            if (start.A == 0)
                start = new Color(end.R, end.G, end.B, 0);

            // If end is fully transparent, replace its RGB with start's RGB
            if (end.A == 0)
                end = new Color(start.R, start.G, start.B, 0);

            var a = HSV.FromColor(start);
            var b = HSV.FromColor(end);
            return HSV.Lerp(a, b, t).ToColor();
        }

        #endregion

        #region Private Methods

        private static StyleValues CreateDefaults()
        {
            var d = new StyleValues();

            // Visual
            d.BackgroundColor = Color.Transparent;
            d.BackgroundGradient = Gradient.None;
            d.BorderColor = Color.Transparent;
            d.BorderWidth = 0.0f;
            d.Rounded = new Float4(0, 0, 0, 0);
            d.BoxShadow = BoxShadow.None;
            d.BackdropBlur = 0.0f;
            d.BackgroundImage = null;

            // Layout
            d.AspectRatio = -1.0f;
            d.Width = UnitValue.Stretch();
            d.Height = UnitValue.Stretch();
            d.MinWidth = UnitValue.Pixels(0);
            d.MaxWidth = UnitValue.Pixels(float.MaxValue);
            d.MinHeight = UnitValue.Pixels(0);
            d.MaxHeight = UnitValue.Pixels(float.MaxValue);
            d.Left = UnitValue.Auto;
            d.Right = UnitValue.Auto;
            d.Top = UnitValue.Auto;
            d.Bottom = UnitValue.Auto;
            d.MinLeft = UnitValue.Pixels(float.MinValue);
            d.MaxLeft = UnitValue.Pixels(float.MaxValue);
            d.MinRight = UnitValue.Pixels(float.MinValue);
            d.MaxRight = UnitValue.Pixels(float.MaxValue);
            d.MinTop = UnitValue.Pixels(float.MinValue);
            d.MaxTop = UnitValue.Pixels(float.MaxValue);
            d.MinBottom = UnitValue.Pixels(float.MinValue);
            d.MaxBottom = UnitValue.Pixels(float.MaxValue);
            d.ChildLeft = UnitValue.Auto;
            d.ChildRight = UnitValue.Auto;
            d.ChildTop = UnitValue.Auto;
            d.ChildBottom = UnitValue.Auto;
            d.RowBetween = UnitValue.Auto;
            d.ColBetween = UnitValue.Auto;
            d.PaddingLeft = UnitValue.Pixels(0);
            d.PaddingRight = UnitValue.Pixels(0);
            d.PaddingTop = UnitValue.Pixels(0);
            d.PaddingBottom = UnitValue.Pixels(0);

            // Transform
            d.TranslateX = 0.0f;
            d.TranslateY = 0.0f;
            d.ScaleX = 1.0f;
            d.ScaleY = 1.0f;
            d.Rotate = 0.0f;
            d.SkewX = 0.0f;
            d.SkewY = 0.0f;
            d.OriginX = 0.5f;
            d.OriginY = 0.5f;
            d.Transform = Transform2D.Identity;

            // Text
            d.TextColor = Color.White;
            d.WordSpacing = 0.0f;
            d.LetterSpacing = 0.0f;
            d.LineHeight = 1.0f;
            d.TabSize = 4;
            d.FontSize = 16.0f;
            d.TextQuality = FontQuality.Normal;

            return d; // mask stays 0: these are defaults, not "explicitly set"
        }

        #endregion

        #region Nested Types

        /// <summary>Persistent interpolation state for one animating property.</summary>
        private sealed class Interp
        {
            public object Start;
            public object Target;
            public object Current;
            public float Time;
        }

        /// <summary>
        /// Out-of-line transition state, allocated only for elements that configure a transition.
        /// Holds the per-frame configs (which properties transition this frame) and the persistent
        /// interpolation state that carries the animated value across frames.
        /// </summary>
        private sealed class Transitions
        {
            private readonly Dictionary<GuiProp, TransitionConfig> _frameConfigs = new();
            private readonly Dictionary<GuiProp, Interp> _interps = new();

            public void BeginFrame() => _frameConfigs.Clear();

            public void Configure(GuiProp property, float duration, Func<float, float>? easing)
                => _frameConfigs[property] = new TransitionConfig { Duration = duration, EasingFunction = easing };

            public void Remove(GuiProp property)
            {
                _frameConfigs.Remove(property);
                _interps.Remove(property);
            }

            public bool IsAnimating(GuiProp property) => _interps.ContainsKey(property);

            /// <summary>
            /// For every property configured with a transition this frame, advance its interpolation
            /// toward the value the builder declared (or the default if it wasn't declared) and write
            /// the tweened result back into the element's current values.
            /// </summary>
            public void Advance(float dt, ref StyleValues current, ElementStyle owner)
            {
                if (_frameConfigs.Count == 0) return;

                foreach (var kv in _frameConfigs)
                {
                    GuiProp property = kv.Key;
                    TransitionConfig config = kv.Value;

                    // The value declared this frame (or the default, since BeginFrame reset unset fields).
                    object target = current.GetBoxed(property);

                    if (!_interps.TryGetValue(property, out var interp))
                    {
                        // First observation of this property - snap, don't animate from nothing.
                        _interps[property] = new Interp { Start = target, Target = target, Current = target, Time = config.Duration };
                        continue;
                    }

                    if (!Equals(interp.Target, target))
                    {
                        // Target changed - restart from the current animated value.
                        interp.Start = interp.Current;
                        interp.Target = target;
                        interp.Time = 0f;
                    }

                    if (interp.Time < config.Duration)
                    {
                        interp.Time += dt;
                        if (interp.Time >= config.Duration)
                        {
                            interp.Current = interp.Target;
                        }
                        else
                        {
                            float t = config.Duration > 0f ? interp.Time / config.Duration : 1f;
                            if (config.EasingFunction != null) t = config.EasingFunction(t);
                            interp.Current = owner.Interpolate(interp.Start, interp.Target, t);
                        }
                    }

                    // Override the declared snap with the animated value.
                    current.Set(property, interp.Current);
                }
            }
        }

        #endregion
    }

    public partial class Paper
    {
        #region Style Management

        /// <summary>
        /// A dictionary to keep track of active styles for each element.
        /// </summary>
        Dictionary<int, ElementStyle> _activeStyles = new Dictionary<int, ElementStyle>();

        /// <summary>
        /// Update the styles for all active elements.
        /// </summary>
        /// <param name="deltaTime">The time since the last frame.</param>
        /// <param name="element">The root element to start updating from.</param>
        private void UpdateStyles(float deltaTime, ElementHandle element)
        {
            int id = element.Data.ID;
            if (_activeStyles.TryGetValue(id, out var style))
            {
                // Update the style properties
                style.Update(deltaTime);
                element.Data._elementStyle = style;
            }
            else
            {
                // Create a new style if it doesn't exist
                style = element.Data._elementStyle ?? new ElementStyle();
                element.Data._elementStyle = style;
                _activeStyles[id] = style;
            }

            // Update Children
            foreach (var childIndex in element.Data.ChildIndices)
            {
                var child = new ElementHandle(this, childIndex);
                UpdateStyles(deltaTime, child);
            }
        }

        /// <summary>
        /// Gets the persistent style for an element id, creating it if needed. This is the single
        /// per-id style instance the builder writes to and layout/render read from, so an element
        /// shares one style across frames instead of allocating a throwaway each frame.
        /// </summary>
        internal ElementStyle GetOrCreateStyle(int elementID)
        {
            if (!_activeStyles.TryGetValue(elementID, out var style))
            {
                style = new ElementStyle();
                _activeStyles[elementID] = style;
            }
            return style;
        }

        /// <summary>
        /// Set a style property value (no transition).
        /// </summary>
        internal void SetStyleProperty(int elementID, GuiProp property, object value)
        {
            GetOrCreateStyle(elementID).SetNextValue(property, value);
        }

        /// <summary>
        /// Configure a transition for a property.
        /// </summary>
        internal void SetTransitionConfig(int elementID, GuiProp property, float duration, Func<float, float>? easing = null)
        {
            GetOrCreateStyle(elementID).SetTransitionConfig(property, duration, easing);
        }

        /// <summary>
        /// Clean up styles at the end of a frame.
        /// </summary>
        private void EndOfFrameCleanupStyles(HashSet<int> createdElements)
        {
            // Drop styles for elements that weren't present this frame. Per-frame reset now happens
            // when the element is (re)created (ElementStyle.BeginFrame), not here.
            List<int> elementsToRemove = new List<int>();
            foreach (var kvp in _activeStyles)
            {
                if (!createdElements.Contains(kvp.Key))
                    elementsToRemove.Add(kvp.Key);
            }

            foreach (var id in elementsToRemove)
                _activeStyles.Remove(id);
        }

        #endregion

        #region Style Templates

        private Dictionary<string, StyleTemplate> _styleTemplates = new Dictionary<string, StyleTemplate>();

        /// <summary>
        /// Creates a new style template.
        /// </summary>
        public StyleTemplate DefineStyle(string name)
        {
            // Create a new style template
            var template = new StyleTemplate();
            _styleTemplates[name] = template;
            return template;
        }

        /// <summary>
        /// Creates a new style template. With one or more parent styles to inherit from.
        /// </summary>
        public StyleTemplate DefineStyle(string name, params string[] inheritFrom)
        {
            // Create a new style template
            var template = new StyleTemplate();

            // Check if the parent style exists
            foreach (var parent in inheritFrom)
                if (_styleTemplates.TryGetValue(parent, out var parentTemplate))
                {
                    parentTemplate.ApplyTo(template);
                }
                else
                {
                    throw new ArgumentException($"Parent style '{parent}' does not exist yet.");
                }

            _styleTemplates[name] = template;
            return template;
        }

        public void RegisterStyle(string name, StyleTemplate template)
        {
            _styleTemplates[name] = template;
        }

        /// <summary>
        /// Creates a new style template.
        /// </summary>
        public bool TryGetStyle(string name, out StyleTemplate? template)
        {
            return _styleTemplates.TryGetValue(name, out template);
        }

        /// <summary>
        /// Applies a named style and its pseudo-states to an element
        /// </summary>
        /// <param name="element">The element to apply styles to</param>
        /// <param name="baseName">The base style name (e.g., "button")</param>
        public void ApplyStyleWithStates(ElementHandle element, string baseName)
        {
            // Apply base style first
            if (TryGetStyle(baseName, out var baseStyle))
            {
                baseStyle.ApplyTo(element);
            }

            // Apply pseudo-states in order
            var pseudoStates = new[]
            {
                ("hovered", IsElementHovered(element.Data.ID)),
                ("focused", IsElementFocused(element.Data.ID)),
                ("active", IsElementActive(element.Data.ID))
            };

            foreach (var (state, isActive) in pseudoStates)
            {
                if (isActive)
                {
                    string pseudoStyleName = $"{baseName}:{state}";
                    if (TryGetStyle(pseudoStyleName, out var pseudoStyle))
                    {
                        pseudoStyle.ApplyTo(element);
                    }
                }
            }
        }

        /// <summary>
        /// Registers a complete style family (base + pseudo-states)
        /// </summary>
        /// <param name="baseName">The base style name</param>
        /// <param name="baseStyle">The base style</param>
        /// <param name="normalStyle">Optional normal state style</param>
        /// <param name="hoveredStyle">Optional hovered state style</param>
        /// <param name="focusedStyle">Optional focused state style</param>
        /// <param name="activeStyle">Optional active state style</param>
        public void RegisterStyleFamily(
            string baseName,
            StyleTemplate baseStyle,
            StyleTemplate normalStyle = null,
            StyleTemplate hoveredStyle = null,
            StyleTemplate focusedStyle = null,
            StyleTemplate activeStyle = null)
        {
            // Register base style
            RegisterStyle(baseName, baseStyle);

            // Register pseudo-states if provided
            if (normalStyle != null)
                RegisterStyle($"{baseName}:normal", normalStyle);

            if (hoveredStyle != null)
                RegisterStyle($"{baseName}:hovered", hoveredStyle);

            if (focusedStyle != null)
                RegisterStyle($"{baseName}:focused", focusedStyle);

            if (activeStyle != null)
                RegisterStyle($"{baseName}:active", activeStyle);
        }

        /// <summary>
        /// Creates a style builder for easier style family creation
        /// </summary>
        /// <param name="baseName">The base style name</param>
        /// <returns>A style family builder</returns>
        public StyleFamilyBuilder CreateStyleFamily(string baseName)
        {
            return new StyleFamilyBuilder(this, baseName);
        }

        /// <summary>
        /// Helper class for building complete style families
        /// </summary>
        public class StyleFamilyBuilder
        {
            private readonly Paper _paper;
            private readonly string _baseName;
            private StyleTemplate _baseStyle;
            private StyleTemplate _normalStyle;
            private StyleTemplate _hoveredStyle;
            private StyleTemplate _focusedStyle;
            private StyleTemplate _activeStyle;

            internal StyleFamilyBuilder(Paper paper, string baseName)
            {
                _paper = paper;
                _baseName = baseName;
            }

            public StyleFamilyBuilder Base(StyleTemplate style)
            {
                _baseStyle = style;
                return this;
            }

            public StyleFamilyBuilder Normal(StyleTemplate style)
            {
                _normalStyle = style;
                return this;
            }

            public StyleFamilyBuilder Hovered(StyleTemplate style)
            {
                _hoveredStyle = style;
                return this;
            }

            public StyleFamilyBuilder Focused(StyleTemplate style)
            {
                _focusedStyle = style;
                return this;
            }

            public StyleFamilyBuilder Active(StyleTemplate style)
            {
                _activeStyle = style;
                return this;
            }

            public void Register()
            {
                _paper.RegisterStyleFamily(_baseName, _baseStyle, _normalStyle, _hoveredStyle, _focusedStyle, _activeStyle);
            }
        }

        #endregion
    }
}
