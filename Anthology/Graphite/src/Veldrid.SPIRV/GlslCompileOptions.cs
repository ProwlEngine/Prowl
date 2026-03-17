using System;

namespace Veldrid.SPIRV
{
    /// <summary>
    /// Controls the options for compiling from GLSL to SPIR-V.
    /// </summary>
    public class GlslCompileOptions
    {
        /// <summary>
        /// Indicates whether the compiled output should preserve debug information. NOTE: If the resulting SPIR-V is intended to
        /// be used as the source of an OpenGL-style GLSL shader, then this property should be set to <see langword="true"/>.
        /// </summary>
        public bool Debug { get; set; }
        /// <summary>
        /// An array of <see cref="MacroDefinition"/> which defines the set of preprocessor macros to define when compiling the
        /// GLSL source code.
        /// </summary>
        public MacroDefinition[] Macros { get; set; }

        /// <summary>
        /// Gets a default <see cref="GlslCompileOptions"/>.
        /// </summary>
        public static GlslCompileOptions Default { get; } = new GlslCompileOptions();

        /// <summary>
        /// Constructs a new <see cref="GlslCompileOptions"/> with default properties.
        /// </summary>
        public GlslCompileOptions()
        {
            Macros = Array.Empty<MacroDefinition>();
        }

        /// <summary>
        /// Constructs a new <see cref="GlslCompileOptions"/>.
        /// </summary>
        public GlslCompileOptions(bool debug, params MacroDefinition[] macros)
        {
            Debug = debug;
            Macros = macros ?? Array.Empty<MacroDefinition>();
        }
    }
}
