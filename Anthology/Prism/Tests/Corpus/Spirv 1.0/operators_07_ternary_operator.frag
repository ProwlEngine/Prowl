#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    bool cond_outer;
    bool cond_inner;
    int int_value;
    float float_value;
};

void main()
{
    // nested ternary with mixed int and float operands promoted to float
    float result = cond_outer ? (cond_inner ? int_value : float_value) : (cond_inner ? float_value : int_value);

    FragColor = vec4(result, 0.0, 0.0, 1.0);
}
