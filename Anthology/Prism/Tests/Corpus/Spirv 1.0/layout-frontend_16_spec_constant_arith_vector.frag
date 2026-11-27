#version 460 core

// Spec constant fed through integer arithmetic into a vector constructor.

layout(constant_id = 0) const int N = 3;

layout(location = 0) out vec4 fragColor;

void main()
{
    int m = N * 2 + 1;
    vec4 c = vec4(float(m), float(N), float(m - N), 1.0);
    fragColor = c;
}
