#version 460 core

#define PI 3.14159265
#define TWO 2.0
#define OFFSET 0.5
#define SCALE 1.5

layout(location=0) out vec4 FragColor;

void main()
{
    float a = PI / TWO;
    float b = OFFSET * SCALE;
    float c = a + b;
    FragColor = vec4(c, OFFSET, SCALE / PI, 1.0);
}
