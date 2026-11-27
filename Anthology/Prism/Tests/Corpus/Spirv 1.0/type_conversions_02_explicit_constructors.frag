#version 460 core

layout(location = 0) out vec4 FragColor;

void main()
{
    float f = 3.99;

    // explicit float -> int truncation
    int i = int(f);

    // explicit float -> uint
    uint u = uint(f);

    // explicit bool() from a nonzero float, never implicit
    bool b = bool(f);

    // explicit float -> double
    double d = double(f);

    float back = float(i) + float(u) + float(b) + float(d);
    FragColor = vec4(back * 0.01, 0.0, 0.0, 1.0);
}
