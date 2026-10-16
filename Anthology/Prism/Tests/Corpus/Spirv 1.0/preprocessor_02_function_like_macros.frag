#version 460 core

#define SQUARE(x) ((x) * (x))
#define ADD2(a, b) ((a) + (b))
#define CLAMP01(x) clamp((x), 0.0, 1.0)

layout(location=0) out vec4 FragColor;

void main()
{
    float x = 0.6;
    float y = 0.3;
    float sq = SQUARE(x);
    float sum = ADD2(x, y);
    float c = CLAMP01(sum);
    FragColor = vec4(sq, sum, c, 1.0);
}
