#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // nested ternary constant folding
    const bool cond_a = true;
    const bool cond_b = false;

    const float final_value = cond_a ? (cond_b ? 1.0 : 2.0) : (cond_b ? 3.0 : 4.0);

    FragColor = (final_value > 1.5) ? vec4(0.0, 1.0, 0.0, 1.0) : vec4(1.0, 0.0, 0.0, 1.0);
}
