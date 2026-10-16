#version 460 core

void main()
{
    dvec3 a = dvec3(1.0lf, 2.0lf, 3.0lf);
    dvec3 b = dvec3(0.5lf, 0.5lf, 0.5lf);
    dvec3 c = a + b;

    dmat4 m = dmat4(1.0lf);
    dvec4 v = dvec4(c, 1.0lf);
    dvec4 transformed = m * v;

    gl_Position = vec4(float(transformed.x), float(transformed.y), float(transformed.z), float(transformed.w));
}
