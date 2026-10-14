#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // integer arithmetic constant folding
    const int a = 7;
    const int b = 3;
    const int c = -2;
    const int d = 5;
    const int e = 4;

    const int result = (a + b) * c - (d / e) + (a % b) - (-c);

    FragColor = vec4(float(result), 0.0, 0.0, 1.0);
}
