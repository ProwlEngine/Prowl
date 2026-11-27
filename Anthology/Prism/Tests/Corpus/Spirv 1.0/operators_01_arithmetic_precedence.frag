#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float a;
    float b;
    float c;
    float d;
    float e;
};

void main()
{
    // operator precedence: multiplication and division bind tighter than +/-
    float unparenthesized = a + b * c - d / e;
    float parenthesized = ((a + b) * (c - d)) / e;

    float diff = unparenthesized - parenthesized;

    FragColor = vec4(diff, unparenthesized, parenthesized, 1.0);
}
