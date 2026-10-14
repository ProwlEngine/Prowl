#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // float arithmetic constant folding
    const float a = 3.5;
    const float b = 2.0;
    const float c = 1.25;
    const float d = 4.0;

    const float result = (a + b) * c - (a - b) / (c + d);

    FragColor = vec4(result, 0.0, 0.0, 1.0);
}
