#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float input_value;
};

void classify_value(float value, out int path_taken)
{
    path_taken = 0;

    if (value > 0.0)
    {
        if (value > 10.0)
        {
            path_taken = 1;
            return;
        }

        path_taken = 2;
        return;
    }

    path_taken = 3;
}

void main()
{
    int path = 0;
    classify_value(input_value, path);

    FragColor = vec4(float(path) * 0.25, 0.0, 0.0, 1.0);
}
