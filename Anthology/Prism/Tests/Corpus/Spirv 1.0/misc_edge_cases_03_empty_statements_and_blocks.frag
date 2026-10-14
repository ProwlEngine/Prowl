#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int loop_count;
};

void main()
{
    ;

    {}

    int counter = 0;
    for (; counter < loop_count; counter++)
        ;

    float result = float(counter) * 0.01;

    FragColor = vec4(result, result, result, 1.0);
}
