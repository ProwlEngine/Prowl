#version 460 core

layout(triangles) in;
layout(triangle_strip, max_vertices = 18) out;

layout(push_constant) uniform PointShadowPushConstant
{
    mat4 faceViewProj[6];
    vec4 lightPositionRange;
    uint instanceIndex;
} pc;

layout(location = 0) in vec3 inWorldPos[];

layout(location = 0) out vec3 outWorldPos;

void main()
{
    for (int face = 0; face < 6; face++)
    {
        gl_Layer = face;
        for (int i = 0; i < 3; i++)
        {
            outWorldPos = inWorldPos[i];
            gl_Position = pc.faceViewProj[face] * vec4(inWorldPos[i], 1.0);
            EmitVertex();
        }
        EndPrimitive();
    }
}
