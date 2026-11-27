#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    bool flag_a;
    bool flag_b;
    bool flag_c;
};

void main()
{
    // logical operators combining uniform booleans, no short circuit reliance
    bool and_result = flag_a && flag_b;
    bool or_result = flag_a || flag_c;
    bool xor_result = flag_b ^^ flag_c;
    bool not_result = !flag_a;

    bool combined = (and_result || not_result) && (or_result ^^ xor_result);

    FragColor = combined ? vec4(1.0, 1.0, 1.0, 1.0) : vec4(0.0, 0.0, 0.0, 1.0);
}
