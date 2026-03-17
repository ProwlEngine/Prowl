using System;

namespace Veldrid.SPIRV
{
    /// <summary>
    /// Controls the parameters of shader translation from SPIR-V to some target language.
    /// </summary>
    public class CrossCompileOptions
    {
        /// <summary>
        /// Indicates whether or not the compiled shader output should include a clip-space Z-range fixup at the end of the
        /// vertex shader.
        /// </summary>
        public bool FixClipSpaceZ { get; set; }
        /// <summary>
        /// Indicates whether or not the compiled shader output should include a fixup at the end of the vertex shader which
        /// inverts the clip-space Y value.
        /// </summary>
        public bool InvertVertexOutputY { get; set; }
        /// <summary>
        /// Indicates whether all resource names should be forced into a normalized form. This has functional impact
        /// on compilation targets where resource names are meaningful, like GLSL.
        /// </summary>
        public bool NormalizeResourceNames { get; set; }
        /// <summary>
        /// An array of <see cref="SpecializationConstant"/> which will be substituted into the shader as new constants.
        /// </summary>
        public SpecializationConstant[] Specializations { get; set; }

        /// <summary>
        /// Constructs a new <see cref="CrossCompileOptions"/> with default values.
        /// </summary>
        public CrossCompileOptions()
        {
            Specializations = Array.Empty<SpecializationConstant>();
        }

        /// <summary>
        /// Constructs a new <see cref="CrossCompileOptions"/>.
        /// </summary>
        public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY)
            : this(fixClipSpaceZ, invertVertexOutputY, Array.Empty<SpecializationConstant>())
        {
        }

        /// <summary>
        /// Constructs a new <see cref="CrossCompileOptions"/>.
        /// </summary>
        public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY, bool normalizeResourceNames)
            : this(fixClipSpaceZ, invertVertexOutputY, normalizeResourceNames, Array.Empty<SpecializationConstant>())
        {
        }

        /// <summary>
        /// Constructs a new <see cref="CrossCompileOptions"/>.
        /// </summary>
        public CrossCompileOptions(bool fixClipSpaceZ, bool invertVertexOutputY, params SpecializationConstant[] specializations)
        {
            FixClipSpaceZ = fixClipSpaceZ;
            InvertVertexOutputY = invertVertexOutputY;
            Specializations = specializations;
        }

        /// <summary>
        /// Constructs a new <see cref="CrossCompileOptions"/>.
        /// </summary>
        public CrossCompileOptions(
            bool fixClipSpaceZ,
            bool invertVertexOutputY,
            bool normalizeResourceNames,
            params SpecializationConstant[] specializations)
        {
            FixClipSpaceZ = fixClipSpaceZ;
            InvertVertexOutputY = invertVertexOutputY;
            NormalizeResourceNames = normalizeResourceNames;
            Specializations = specializations;
        }
    }
}
