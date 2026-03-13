#version 450

#define PARTICLE_COUNT 1024

struct ParticleInfo
{
    vec2 Position;
    vec2 Velocity;
    vec4 Color;
};

layout(std140, set = 0, binding = 0) buffer ParticlesBuffer
{
    ParticleInfo Particles[];
};

layout(set = 1, binding = 0) uniform ScreenSizeBuffer
{
    float ScreenWidth;
    float ScreenHeight;
    vec2 Padding_;
};

layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

void main()
{
    uint index = gl_GlobalInvocationID.x;
    if (index > PARTICLE_COUNT)
    {
        return;
    }

    vec2 pos = Particles[index].Position;
    vec2 vel = Particles[index].Velocity;

    vec2 newPos = pos + vel;
    vec2 newVel = vel;
    if (newPos.x > ScreenWidth)
    {
        newPos.x -= (newPos.x - ScreenWidth);
        newVel.x *= -1;
    }
    if (newPos.x < 0)
    {
        newPos.x *= -1;
        newVel.x *= -1;
    }
    if (newPos.y > ScreenHeight)
    {
        newPos.y -= (newPos.y - ScreenHeight);
        newVel.y *= -1;
    }
    if (newPos.y < 0)
    {
        newPos.y = -newPos.y;
        newVel.y *= -1;
    }

    Particles[index].Position = newPos;
    Particles[index].Velocity = newVel;
}
