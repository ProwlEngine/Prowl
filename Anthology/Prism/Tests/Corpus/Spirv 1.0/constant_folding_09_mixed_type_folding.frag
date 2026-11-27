#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // mixed type constant folding via explicit constructors
    const float x = float(3) + 2.5;
    const int y = int(4.9) - 2;
    const uint z = uint(y) + 3u;
    const double dbl_part = 1.5lf + double(int(2));
    const float w = float(z) * float(int(2)) - float(dbl_part);

    const float result = x + float(y) + float(z) + w;

    FragColor = vec4(result, 0.0, 0.0, 1.0);
}
