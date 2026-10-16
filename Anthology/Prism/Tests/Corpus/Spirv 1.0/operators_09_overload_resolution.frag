#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int int_uniform;
};

float combine(float a, float b)
{
    return a + b;
}

vec3 combine(vec3 a, vec3 b)
{
    return a + b;
}

int combine(int a, int b)
{
    return a + b;
}

void main()
{
    // overload resolution across float, vec3 and int parameter lists
    float float_result = combine(1.5, 2.5);
    vec3 vec_result = combine(vec3(1.0, 2.0, 3.0), vec3(4.0, 5.0, 6.0));
    int int_result = combine(int_uniform, 3);

    // relies on int to float implicit promotion to select the float overload
    float promoted_result = combine(int_uniform, 2.5);

    float total = float_result + vec_result.x + vec_result.y + vec_result.z + float(int_result) + promoted_result;

    FragColor = vec4(total, 0.0, 0.0, 1.0);
}
