#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // uint arithmetic constant folding including wraparound
    const uint a = 5u;
    const uint b = 9u;
    const uint wrapped = 0u - 1u;
    const uint result = (a + b) * 2u - wrapped % 100u;

    FragColor = vec4(float(result), 0.0, 0.0, 1.0);
}
