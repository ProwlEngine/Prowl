#version 460 core

layout(location=0) in VertexData
{
    vec3 normal;
    vec2 uv;
} v_in;

layout(location=0) out vec4 FragColor;

void main()
{
    FragColor = vec4(v_in.normal, v_in.uv.x + v_in.uv.y);
}
