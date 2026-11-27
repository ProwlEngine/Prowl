#version 460 core

layout(location=0) out vec4 FragColor;

const float A = 2.0;
const float B = A * 3.0;
const float C = A + B;
const float D = C - B + A;

void main()
{
    FragColor = vec4(A * 0.1, B * 0.1, C * 0.1, D * 0.1);
}
