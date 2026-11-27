#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float float_value;
    int int_bits_value;
    uint uint_bits_value;
};

void main()
{
    int as_int = floatBitsToInt(float_value);
    float back_to_float = intBitsToFloat(as_int);

    uint as_uint = floatBitsToUint(float_value);
    float from_uint = uintBitsToFloat(as_uint);

    float from_int_bits = intBitsToFloat(int_bits_value);
    float from_uint_bits = uintBitsToFloat(uint_bits_value);

    FragColor = vec4(back_to_float, from_uint, from_int_bits, from_uint_bits);
}
