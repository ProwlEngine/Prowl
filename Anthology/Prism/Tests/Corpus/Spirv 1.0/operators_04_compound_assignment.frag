#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int seed;
};

void main()
{
    // compound assignment operators applied in sequence
    float f = float(seed) + 1.0;
    f += 3.0;
    f -= 1.5;
    f *= 2.0;
    f /= 4.0;

    int i = seed;
    i %= 5;

    int mask = seed | 0x0F;
    mask &= 0x3C;
    mask |= 0x01;
    mask ^= 0x0F;
    mask <<= 2;
    mask >>= 1;

    FragColor = vec4(f, float(i), float(mask), 1.0);
}
