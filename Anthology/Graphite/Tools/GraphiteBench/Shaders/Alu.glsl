Shared
{
    uniform vec4 Tint;
    uniform int Iterations;
}

Vertex
{
    in vec3 POSITION0;
    in vec2 UV0;

    out vec2 uv;

    void main()
    {
        gl_Position = vec4(POSITION0.xy, 0.0, 1.0);
        uv = UV0;
    }
}

Fragment
{
    in vec2 uv;

    out vec4 target;

    void main()
    {
        vec2 z = uv * 2.0 - 1.0;
        vec3 acc = vec3(0.0);
        for (int i = 0; i < Iterations; i++)
        {
            z = vec2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + uv * 0.37;
            z = vec2(sin(z.x * 3.1 + float(i)), cos(z.y * 2.7 - float(i)));
            acc += vec3(exp(-abs(z.x)), sqrt(abs(z.y)), atan(z.y, z.x + 1.5)) * 0.01;
        }
        target = vec4(acc, 1.0) * Tint;
    }
}
