#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // boolean constant folding with relational operators
    const int a = 5;
    const int b = 3;
    const float c = 1.5;
    const float d = 2.5;

    const bool cond1 = (a > b) && (c < d);
    const bool cond2 = (a == b) || (c != d);
    const bool cond3 = cond1 ^^ cond2;
    const bool final_cond = !cond3 && (a >= b) && (d <= 3.0 || c >= 1.0);

    FragColor = final_cond ? vec4(1.0, 0.0, 0.0, 1.0) : vec4(0.0, 1.0, 0.0, 1.0);
}
