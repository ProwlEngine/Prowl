#version 460 core
#extension GL_NV_geometry_shader_passthrough : require
#extension GL_ARB_shader_viewport_layer_array : require

// NV geometry passthrough: members are copied automatically, no EmitVertex.

layout(triangles) in;

layout(passthrough) in gl_PerVertex
{
    vec4 gl_Position;
};

layout(passthrough, location = 0) in Inputs
{
    vec2 texcoord;
};

void main()
{
    gl_Layer = gl_PrimitiveIDIn;
}
