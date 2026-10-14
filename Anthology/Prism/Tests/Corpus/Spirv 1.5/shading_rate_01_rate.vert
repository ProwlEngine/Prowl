#version 460
#extension GL_EXT_fragment_shading_rate : require
layout(location = 0) in vec3 position;
void main()
{
    gl_Position = vec4(position, 1.0);
    gl_PrimitiveShadingRateEXT = gl_ShadingRateFlag2VerticalPixelsEXT | gl_ShadingRateFlag2HorizontalPixelsEXT;
}
