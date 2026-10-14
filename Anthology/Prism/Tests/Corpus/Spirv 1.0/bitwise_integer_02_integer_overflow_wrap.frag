#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    uint small_uint;
    uint large_uint;
    int max_int_uniform;
    int add_amount;
};

void main()
{
    uint wrapped = small_uint - large_uint;
    int overflowed = max_int_uniform + add_amount;

    float r = float(wrapped & 0xFFu) / 255.0;
    float g = float(uint(overflowed) & 0xFFu) / 255.0;

    FragColor = vec4(r, g, 0.0, 1.0);
}
