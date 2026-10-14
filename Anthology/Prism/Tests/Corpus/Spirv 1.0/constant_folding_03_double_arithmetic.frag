#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // double arithmetic constant folding using lf suffix
    const double a = 2.5lf;
    const double b = 4.0lf;
    const double c = 1.5lf;

    const double result = (a + b) * c - (b / a);

    FragColor = vec4(float(result), 0.0, 0.0, 1.0);
}
