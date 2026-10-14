#version 460 core

layout(location=0) out vec4 FragColor;

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
    float f = combine(1.0, 2.0);
    vec3 v = combine(vec3(0.1, 0.2, 0.3), vec3(0.4, 0.1, 0.2));
    int i = combine(2, 3);

    FragColor = vec4(v, 0.0) + vec4(f * 0.1, 0.0, 0.0, float(i) * 0.1);
}
