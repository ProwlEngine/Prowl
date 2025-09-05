using System.Drawing;

using Prowl.Paper.Utilities;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;
using Prowl.Vector;

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

        // Border spacing
        BorderLeft,
        BorderRight,
        BorderTop,
        BorderBottom,
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

        #region Text Properties
        TextColor,

        WordSpacing,
        LetterSpacing,
        LineHeight,

        TabSize,
        FontSize,
        #endregion
    }

    /// <summary>
    /// Builds transformation matrices for UI elements.
    /// </summary>
    public class TransformBuilder
    {
        #region Fields

        private double _translateX = 0;
        private double _translateY = 0;
        private double _scaleX = 1;
        private double _scaleY = 1;
        private double _rotate = 0;
        private double _skewX = 0;
        private double _skewY = 0;
        private double _originX = 0.5f; // Default to center (50%)
        private double _originY = 0.5f; // Default to center (50%)
        private Transform2D? _customTransform = null;

        #endregion

        #region Builder Methods

        /// <summary>
        /// Sets the X translation.
        /// </summary>
        public TransformBuilder SetTranslateX(double x)
        {
            _translateX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y translation.
        /// </summary>
        public TransformBuilder SetTranslateY(double y)
        {
            _translateY = y;
            return this;
        }

        /// <summary>
        /// Sets the X scale factor.
        /// </summary>
        public TransformBuilder SetScaleX(double x)
        {
            _scaleX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y scale factor.
        /// </summary>
        public TransformBuilder SetScaleY(double y)
        {
            _scaleY = y;
            return this;
        }

        /// <summary>
        /// Sets the rotation angle in degrees.
        /// </summary>
        public TransformBuilder SetRotate(double angleInDegrees)
        {
            _rotate = angleInDegrees;
            return this;
        }

        /// <summary>
        /// Sets the X skew angle.
        /// </summary>
        public TransformBuilder SetSkewX(double angle)
        {
            _skewX = angle;
            return this;
        }

        /// <summary>
        /// Sets the Y skew angle.
        /// </summary>
        public TransformBuilder SetSkewY(double angle)
        {
            _skewY = angle;
            return this;
        }

        /// <summary>
        /// Sets the X origin point (0-1 range).
        /// </summary>
        public TransformBuilder SetOriginX(double x)
        {
            _originX = x;
            return this;
        }

        /// <summary>
        /// Sets the Y origin point (0-1 range).
        /// </summary>
        public TransformBuilder SetOriginY(double y)
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
            double originX = rect.x + _originX * rect.width;
            double originY = rect.y + _originY * rect.height;

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
                transformMatrix *= Transform2D.CreateRotate(_rotate);

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
            result = originMatrix * transformMatrix * Transform2D.CreateTranslation(originX, originY);

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
        public double Duration { get; set; }

        /// <summary>Optional easing function to control transition timing.</summary>
        public Func<double, double>? EasingFunction { get; set; }
    }

    /// <summary>
    /// Manages styling and transitions for UI elements.
    /// </summary>
    internal class ElementStyle
    {
        #region Fields

        // State tracking
        private HashSet<GuiProp> _propertiesSetThisFrame = new();
        private HashSet<GuiProp> _propertiesWithTransitions = new();
        private bool _firstFrame = true;

        // Property values
        private Dictionary<GuiProp, object> _currentValues = new();
        private Dictionary<GuiProp, object> _targetValues = new();

        // Transition state
        private Dictionary<GuiProp, TransitionConfig> _transitionConfigs = new();
        private Dictionary<GuiProp, InterpolationState> _interpolations = new();

        // Inheritance
        private ElementStyle? _parent;

        private static object[] _defaultValues;
        private static bool _initialized = false;

        #endregion

        #region Public Methods

        /// <summary>
        /// Marks the end of a frame, resetting per-frame state.
        /// </summary>
        public void EndOfFrame()
        {
            _propertiesSetThisFrame.Clear();
            _firstFrame = false;
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
        public bool HasValue(GuiProp property) => _currentValues.ContainsKey(property);

        /// <summary>
        /// Gets the current value of a property, falling back to parent or default.
        /// </summary>
        public object GetValue(GuiProp property)
        {
            // If we have the value, return it
            if (_currentValues.TryGetValue(property, out var value))
                return value;

            // Otherwise check parent
            if (_parent != null)
                return _parent.GetValue(property);

            // Otherwise return default
            return GetDefaultValue(property);
        }

        /// <summary>
        /// Sets a property value directly without transition.
        /// </summary>
        public void SetDirectValue(GuiProp property, object value)
        {
            _propertiesSetThisFrame.Add(property);

            // Set the value directly without transition
            _currentValues[property] = value;
            _targetValues[property] = value; // Ensure target matches current
            _interpolations.Remove(property); // Remove any existing interpolation state
        }

        /// <summary>
        /// Sets a property's target value for transition.
        /// </summary>
        public void SetNextValue(GuiProp property, object value)
        {
            _propertiesSetThisFrame.Add(property);

            // Store the target value - this is where we want to end up
            _targetValues[property] = value;
        }

        /// <summary>
        /// Configures a transition for a property.
        /// </summary>
        public void SetTransitionConfig(GuiProp property, double duration, Func<double, double>? easing = null)
        {
            // Store the transition configuration for this property
            _transitionConfigs[property] = new TransitionConfig {
                Duration = duration,
                EasingFunction = easing
            };

            // Mark this property as having a transition
            _propertiesWithTransitions.Add(property);
        }

        /// <summary>
        /// Removes a property value and any related transition state.
        /// </summary>
        public void ClearValue(GuiProp property)
        {
            _currentValues.Remove(property);
            _targetValues.Remove(property);
            _transitionConfigs.Remove(property);
            _interpolations.Remove(property);
        }

        /// <summary>
        /// Updates all property transitions for the current frame.
        /// </summary>
        public void Update(double deltaTime)
        {
            if (!_firstFrame)
            {
                // Initialize values for properties with transitions
                InitializeTransitionProperties();
            }

            // Track completed transitions for cleanup
            List<GuiProp> completedInterpolations = new List<GuiProp>();

            // Process all properties that have target values
            foreach (var property in _targetValues.Keys)
            {
                // Get the target value based on what was set this frame or inherited
                object targetValue = GetTargetValue(property);

                // If the property has a transition config, set up an interpolation
                if (_transitionConfigs.TryGetValue(property, out var config))
                {
                    ProcessPropertyWithTransition(property, targetValue, config, deltaTime, completedInterpolations);
                }
                else
                {
                    // No transition config, set immediately
                    _currentValues[property] = targetValue;
                }
            }

            // Clean up completed interpolations
            foreach (var property in completedInterpolations)
            {
                _interpolations.Remove(property);
            }

            // Clear transition configs after processing - they don't persist across frames
            _transitionConfigs.Clear();
        }

        /// <summary>
        /// Gets the complete transform for an element.
        /// </summary>
        public Transform2D GetTransformForElement(Rect rect)
        {
            TransformBuilder builder = new TransformBuilder();

            // Set transform properties from the current values
            if (_currentValues.TryGetValue(GuiProp.TranslateX, out var translateX))
                builder.SetTranslateX((double)translateX);

            if (_currentValues.TryGetValue(GuiProp.TranslateY, out var translateY))
                builder.SetTranslateY((double)translateY);

            if (_currentValues.TryGetValue(GuiProp.ScaleX, out var scaleX))
                builder.SetScaleX((double)scaleX);

            if (_currentValues.TryGetValue(GuiProp.ScaleY, out var scaleY))
                builder.SetScaleY((double)scaleY);

            if (_currentValues.TryGetValue(GuiProp.Rotate, out var rotate))
                builder.SetRotate((double)rotate);

            if (_currentValues.TryGetValue(GuiProp.SkewX, out var skewX))
                builder.SetSkewX((double)skewX);

            if (_currentValues.TryGetValue(GuiProp.SkewY, out var skewY))
                builder.SetSkewY((double)skewY);

            if (_currentValues.TryGetValue(GuiProp.OriginX, out var originX))
                builder.SetOriginX((double)originX);

            if (_currentValues.TryGetValue(GuiProp.OriginY, out var originY))
                builder.SetOriginY((double)originY);

            if (_currentValues.TryGetValue(GuiProp.Transform, out var customTransform))
                builder.SetCustomTransform((Transform2D)customTransform);

            return builder.Build(rect);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Initializes values for properties with transitions.
        /// </summary>
        private void InitializeTransitionProperties()
        {
            foreach (var property in _propertiesWithTransitions)
            {
                // If we don't have a current value yet for a property with transition,
                // initialize it with the default or parent value
                if (!_currentValues.ContainsKey(property))
                {
                    if (_parent != null && _parent.HasValue(property))
                        _currentValues[property] = _parent.GetValue(property);
                    else
                        _currentValues[property] = GetDefaultValue(property);
                }
            }
        }

        /// <summary>
        /// Gets the target value for a property based on explicit setting or inheritance.
        /// </summary>
        private object GetTargetValue(GuiProp property)
        {
            if (_propertiesSetThisFrame.Contains(property)) // If property was set this frame, use the explicit value
                return _targetValues[property];
            else if (_parent != null && _parent.HasValue(property)) // If not set, but has parent, use parent value
                return _parent.GetValue(property);
            else // If not set and no parent, use default value
                return GetDefaultValue(property);
        }

        /// <summary>
        /// Processes transitions for a property.
        /// </summary>
        private void ProcessPropertyWithTransition(GuiProp property, object targetValue, TransitionConfig config,
            double deltaTime, List<GuiProp> completedInterpolations)
        {
            // If we don't have a current value yet, initialize it immediately
            if (!_currentValues.TryGetValue(property, out object? currentValue))
            {
                currentValue = targetValue;
                _currentValues[property] = currentValue;
                return;
            }

            // Skip if the values are already equal
            if (currentValue.Equals(targetValue))
                return;

            // Create or update interpolation state
            if (!_interpolations.TryGetValue(property, out var state))
            {
                state = new InterpolationState {
                    StartValue = currentValue,
                    TargetValue = targetValue,
                    Duration = config.Duration,
                    EasingFunction = config.EasingFunction,
                    CurrentTime = 0
                };
                _interpolations[property] = state;
            }
            else if (!state.TargetValue.Equals(targetValue))
            {
                // Target has changed, restart interpolation
                state.StartValue = currentValue;
                state.TargetValue = targetValue;
                state.Duration = config.Duration;
                state.EasingFunction = config.EasingFunction;
                state.CurrentTime = 0;
            }

            // Update the interpolation
            state.CurrentTime += deltaTime;

            if (state.CurrentTime >= state.Duration)
            {
                // Interpolation complete
                _currentValues[property] = targetValue;
                completedInterpolations.Add(property);
            }
            else
            {
                // Calculate interpolated value
                double t = state.CurrentTime / state.Duration;
                if (state.EasingFunction != null)
                    t = state.EasingFunction(t);

                _currentValues[property] = Interpolate(state.StartValue, state.TargetValue, t);
            }
        }

        /// <summary>
        /// Interpolates between two values based on their type.
        /// </summary>
        private object Interpolate(object start, object end, double t)
        {
            if (start is double doubleStart && end is double doubleEnd)
            {
                return doubleStart + (doubleEnd - doubleStart) * t;
            }
            else if(start is float floatStart && end is float floatEnd)
            {
                return floatStart + (floatEnd - floatStart) * t;
            }
            else if (start is int intStart && end is int intEnd)
            {
                return intStart + (int)((intEnd - intStart) * t);
            }
            else if (start is Color colorStart && end is Color colorEnd)
            {
                return InterpolateColor(colorStart, colorEnd, t);
            }
            else if (start is Vector2 vectorStart && end is Vector2 vectorEnd)
            {
                return Vector2.Lerp(vectorStart, vectorEnd, t);
            }
            else if (start is Vector3 vector3Start && end is Vector3 vector3End)
            {
                return Vector3.Lerp(vector3Start, vector3End, t);
            }
            else if (start is Vector4 vector4Start && end is Vector4 vector4End)
            {
                return Vector4.Lerp(vector4Start, vector4End, t);
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
        private Color InterpolateColor(Color start, Color end, double t)
        {
            // If start is fully transparent, replace its RGB with end's RGB
            if (start.A == 0)
                start = Color.FromArgb(0, end.R, end.G, end.B);

            // If end is fully transparent, replace its RGB with start's RGB
            if (end.A == 0)
                end = Color.FromArgb(0, start.R, start.G, start.B);

            var a = HSV.FromColor(start);
            var b = HSV.FromColor(end);
            return HSV.Lerp(a, b, t).ToColor();
        }

        /// <summary>
        /// Gets the default value for a property.
        /// </summary>
        private object GetDefaultValue(GuiProp property)
        {
            InitializeDefaults();
            int index = (int)property;
            return _defaultValues[index];
        }

        #endregion

        #region Private Methods

        public static void InitializeDefaults()
        {
            if (_initialized) return;

            // Assuming GuiProp enum values are contiguous starting from 0
            int maxEnumValue = Enum.GetValues<GuiProp>().Max(x => (int)x);
            _defaultValues = new object[maxEnumValue + 1];

            // Visual Properties
            _defaultValues[(int)GuiProp.BackgroundColor] = Color.Transparent;
            _defaultValues[(int)GuiProp.BackgroundGradient] = Gradient.None;
            _defaultValues[(int)GuiProp.BorderColor] = Color.Transparent;
            _defaultValues[(int)GuiProp.BorderWidth] = 0.0;
            _defaultValues[(int)GuiProp.Rounded] = new Vector4(0, 0, 0, 0);
            _defaultValues[(int)GuiProp.BoxShadow] = BoxShadow.None;

            // Core Layout Properties
            _defaultValues[(int)GuiProp.AspectRatio] = -1.0;
            _defaultValues[(int)GuiProp.Width] = UnitValue.Stretch();
            _defaultValues[(int)GuiProp.Height] = UnitValue.Stretch();
            _defaultValues[(int)GuiProp.MinWidth] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.MaxWidth] = UnitValue.Pixels(double.MaxValue);
            _defaultValues[(int)GuiProp.MinHeight] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.MaxHeight] = UnitValue.Pixels(double.MaxValue);

            // Positioning Properties
            _defaultValues[(int)GuiProp.Left] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.Right] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.Top] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.Bottom] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.MinLeft] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.MaxLeft] = UnitValue.Pixels(double.MaxValue);
            _defaultValues[(int)GuiProp.MinRight] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.MaxRight] = UnitValue.Pixels(double.MaxValue);
            _defaultValues[(int)GuiProp.MinTop] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.MaxTop] = UnitValue.Pixels(double.MaxValue);
            _defaultValues[(int)GuiProp.MinBottom] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.MaxBottom] = UnitValue.Pixels(double.MaxValue);

            // Child Layout Properties
            _defaultValues[(int)GuiProp.ChildLeft] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.ChildRight] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.ChildTop] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.ChildBottom] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.RowBetween] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.ColBetween] = UnitValue.Auto;
            _defaultValues[(int)GuiProp.BorderLeft] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.BorderRight] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.BorderTop] = UnitValue.Pixels(0);
            _defaultValues[(int)GuiProp.BorderBottom] = UnitValue.Pixels(0);

            // Transform Properties
            _defaultValues[(int)GuiProp.TranslateX] = 0.0;
            _defaultValues[(int)GuiProp.TranslateY] = 0.0;
            _defaultValues[(int)GuiProp.ScaleX] = 1.0;
            _defaultValues[(int)GuiProp.ScaleY] = 1.0;
            _defaultValues[(int)GuiProp.Rotate] = 0.0;
            _defaultValues[(int)GuiProp.SkewX] = 0.0;
            _defaultValues[(int)GuiProp.SkewY] = 0.0;
            _defaultValues[(int)GuiProp.OriginX] = 0.5;
            _defaultValues[(int)GuiProp.OriginY] = 0.5;
            _defaultValues[(int)GuiProp.Transform] = Transform2D.Identity;

            // Text Properties
            _defaultValues[(int)GuiProp.TextColor] = Color.White;
            _defaultValues[(int)GuiProp.WordSpacing] = 0.0;
            _defaultValues[(int)GuiProp.LetterSpacing] = 0.0;
            _defaultValues[(int)GuiProp.LineHeight] = 1.0;
            _defaultValues[(int)GuiProp.TabSize] = 4;
            _defaultValues[(int)GuiProp.FontSize] = 16.0;

            _initialized = true;
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Helper class to track interpolation state.
        /// </summary>
        private class InterpolationState
        {
            public object StartValue { get; set; }
            public object TargetValue { get; set; }
            public double Duration { get; set; }
            public Func<double, double>? EasingFunction { get; set; }
            public double CurrentTime { get; set; }
        }

        #endregion
    }

    public partial class Paper
    {
        #region Style Management

        /// <summary>
        /// A dictionary to keep track of active styles for each element.
        /// </summary>
        Dictionary<ulong, ElementStyle> _activeStyles = new();

        /// <summary>
        /// Update the styles for all active elements.
        /// </summary>
        /// <param name="deltaTime">The time since the last frame.</param>
        /// <param name="element">The root element to start updating from.</param>
        private void UpdateStyles(double deltaTime, ElementHandle element)
        {
            ulong id = element.Data.ID;
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
        /// Set a style property value (no transition).
        /// </summary>
        internal void SetStyleProperty(ulong elementID, GuiProp property, object value)
        {
            if (!_activeStyles.TryGetValue(elementID, out var style))
            {
                // Create a new style if it doesn't exist
                style = new ElementStyle();
                _activeStyles[elementID] = style;
            }

            // Set the next value
            style.SetNextValue(property, value);
        }

        /// <summary>
        /// Configure a transition for a property.
        /// </summary>
        internal void SetTransitionConfig(ulong elementID, GuiProp property, double duration, Func<double, double>? easing = null)
        {
            if (!_activeStyles.TryGetValue(elementID, out var style))
            {
                // Create a new style if it doesn't exist
                style = new ElementStyle();
                _activeStyles[elementID] = style;
            }

            // Set up the transition configuration
            style.SetTransitionConfig(property, duration, easing);
        }

        /// <summary>
        /// Clean up styles at the end of a frame.
        /// </summary>
        private void EndOfFrameCleanupStyles(HashSet<ulong> createdElements)
        {
            // Clean up any elements that haven't been accessed this frame
            List<ulong> elementsToRemove = new List<ulong>();
            foreach (var kvp in _activeStyles)
            {
                if (!createdElements.Contains(kvp.Key))
                    elementsToRemove.Add(kvp.Key);
                else
                    kvp.Value.EndOfFrame(); // Reset the style for the next frame
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
