namespace Veldrid.SPIRV
{
    /// <summary>
    /// Represents a single preprocessor macro used when compiling shader source code.
    /// </summary>
    public class MacroDefinition
    {
        /// <summary>
        /// The name of the macro.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The macro's replacement value. May be null.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Constructs a new <see cref="MacroDefinition"/> with no value.
        /// </summary>
        public MacroDefinition(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Constructs a new <see cref="MacroDefinition"/> with a value.
        /// </summary>
        public MacroDefinition(string name, string value)
        {
            Name = name;
            Value = value;
        }

        internal MacroDefinition() { }
    }
}
