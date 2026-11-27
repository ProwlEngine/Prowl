#version 460 core

void main()
{
    double a = 2.5lf;
    double b = 4.0lf;
    double c = a + b;
    double d = c - a;
    double e = d * b;
    double f = e / c;

    float result = float(f);
    gl_Position = vec4(result, result, 0.0, 1.0);
}
