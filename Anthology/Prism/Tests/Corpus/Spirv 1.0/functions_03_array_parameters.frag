#version 460 core

layout(location=0) out vec4 FragColor;

float[4] scale_array(float[4] values)
{
    float[4] result;
    for (int i = 0; i < 4; i++)
    {
        result[i] = values[i] * 2.0;
    }
    return result;
}

void main()
{
    float[4] input_values = float[4](1.0, 2.0, 3.0, 4.0);
    float[4] scaled = scale_array(input_values);

    float total = 0.0;
    for (int i = 0; i < 4; i++)
    {
        total += scaled[i];
    }

    FragColor = vec4(total * 0.05, 0.0, 0.0, 1.0);
}
