Shared
{
    uniform vec4 Tint;
    uniform int Iterations;
}

Vertex
{
    in vec3 POSITION0;
    in vec2 UV0;

    out vec4 color;

    void main()
    {
        vec3 p = POSITION0;
        vec3 n = vec3(UV0, 1.0);
        for (int i = 0; i < Iterations; i++)
        {
            n = vec3(sin(n.y * 1.7 + float(i)), cos(n.z * 2.3 - float(i)), sin(n.x * 0.9 + n.y));
            p.z += n.x * 0.001;
        }

        gl_Position = vec4(p.xy, clamp(p.z * 0.5 + 0.5, 0.0, 1.0), 1.0);
        color = vec4(n * 0.5 + 0.5, 1.0);
    }
}

Fragment
{
    in vec4 color;

    out vec4 target;

    void main()
    {
        target = color * Tint;
    }
}
