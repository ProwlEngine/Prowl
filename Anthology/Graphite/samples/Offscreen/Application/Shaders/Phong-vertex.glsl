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
layout(location = 0) out vec3 fsin_normal;
layout(location = 1) out vec3 fsin_color;
layout(location = 2) out vec3 fsin_eyePos;
layout(location = 3) out vec3 fsin_lightVec;

void main()
{
    vec4 v4Pos = vec4(Position, 1);
    fsin_normal = Normal;
    fsin_color = Color;
    gl_Position = Projection * View * (Model * v4Pos);
    vec4 eyePos = View * Model * v4Pos;
    fsin_eyePos = eyePos.xyz;
    vec4 eyeLightPos = View * LightPos;
    fsin_lightVec = normalize(LightPos.xyz - fsin_eyePos);
}
