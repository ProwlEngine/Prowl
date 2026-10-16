#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int int_a;
    int int_b;
    uint uint_a;
    uint uint_b;
};

void main()
{
    int and_result = int_a & int_b;
    int or_result = int_a | int_b;
    int xor_result = int_a ^ int_b;
    int not_result = ~int_a;
    int shl_result = int_a << 2;
    int shr_result = int_b >> 1;

    uint uand_result = uint_a & uint_b;
    uint uor_result = uint_a | uint_b;

    float r = float(and_result & 0xFF) / 255.0;
    float g = float(or_result & 0xFF) / 255.0;
    float bl = float(xor_result ^ not_result) / 255.0;
    float a = float((shl_result + shr_result + int(uand_result) + int(uor_result)) & 0xFF) / 255.0;

    FragColor = vec4(r, g, bl, a);
}
