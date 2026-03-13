#version 450

layout(set = 0, binding = 1) uniform ScreenSizeBuffer
{
    float ScreenWidth;
    float ScreenHeight;
    vec2 Padding_;
};

layout(set = 0, binding = 2) uniform ShiftBuffer
{
    float RShift;
    float GShift;
    float BShift;
    float Padding1_;
};

layout(set = 0, binding = 0, rgba32f) uniform image2D Tex;

layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

void main()
{
    float x = (gl_GlobalInvocationID.x + RShift);
    float y = (gl_GlobalInvocationID.y + GShift);

    imageStore(Tex, ivec2(gl_GlobalInvocationID.xy), vec4(x / ScreenWidth, y / ScreenHeight, BShift, 1));
}
