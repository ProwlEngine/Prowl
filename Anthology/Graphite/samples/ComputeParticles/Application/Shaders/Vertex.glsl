#version 450

struct ParticleInfo
{
    vec2 Position;
    vec2 Velocity;
    vec4 Color;
};

layout(std140, set = 0, binding = 0) readonly buffer ParticlesBuffer
{
    ParticleInfo Particles[];
};

layout(set = 1, binding = 0) uniform ScreenSizeBuffer
{
    float ScreenWidth;
    float ScreenHeight;
    vec2 Padding_;
};

layout (location = 0) out vec4 fsin_color;



void main()
{
    gl_PointSize = 1;
    gl_Position = vec4(Particles[gl_VertexIndex].Position / vec2(ScreenWidth, ScreenHeight), 0, 1);
    gl_Position.xy = 2 * (gl_Position.xy - vec2(0.5, 0.5));
    fsin_color = Particles[gl_VertexIndex].Color;
}
