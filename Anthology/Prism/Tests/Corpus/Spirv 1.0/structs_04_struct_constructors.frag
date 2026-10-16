#version 460 core

layout(location=0) out vec4 FragColor;

// struct constructor syntax StructName(args)
struct point_t {
    vec2 position;
    float radius;
};

void main()
{
    point_t a = point_t(vec2(0.1, 0.2), 1.0);
    point_t b = point_t(vec2(0.4, 0.6), 2.0);

    point_t c = point_t(a.position + b.position, a.radius + b.radius);

    FragColor = vec4(c.position, c.radius * 0.1, 1.0);
}
