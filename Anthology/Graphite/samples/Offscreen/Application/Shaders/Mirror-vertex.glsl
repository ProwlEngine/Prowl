#version 450

layout(set = 0, binding = 0) uniform UBO
{
    mat4 Projection;
    mat4 View;
    mat4 Model;
    vec4 LightPos;
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 UV;
layout(location = 2) in vec3 Color;
layout(location = 3) in vec3 Normal;
layout(location = 0) out vec2 fsin_uv;
layout(location = 1) out vec4 fsin_position;

void main()
{
    fsin_uv = UV;
    fsin_position = Projection * View * Model * vec4(Position, 1.f);
    gl_Position = fsin_position;
}
