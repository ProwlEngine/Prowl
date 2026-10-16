#version 460 core

layout(location = 0) out vec4 FragColor;

void main()
{
    // int -> uint implicit
    int i = 5;
    uint u = i;

    // int -> float implicit
    float f = i;

    // int -> double implicit
    double d = i;

    // uint -> float implicit
    uint u2 = 3u;
    float f2 = u2;

    // uint -> double implicit
    double d2 = u2;

    // float -> double implicit
    float f3 = 2.5;
    double d3 = f3;

    float total = float(u) + f + float(d) + f2 + float(d2) + f3 + float(d3);
    FragColor = vec4(total * 0.01, 0.0, 0.0, 1.0);
}
