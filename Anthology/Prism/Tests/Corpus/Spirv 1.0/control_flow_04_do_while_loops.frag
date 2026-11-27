#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float start_value;
};

void main()
{
    float value = start_value;
    int iterations = 0;

    // executes at least once regardless of start_value
    do
    {
        value += 1.0;
        iterations = iterations + 1;
    } while (value < 0.0);

    FragColor = vec4(value * 0.01, float(iterations) * 0.01, 0.0, 1.0);
}
