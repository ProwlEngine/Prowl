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
    uniform vec4 Tint;
    uniform int Iterations;
    uniform sampler2D Noise;

    in vec2 uv;

    out vec4 target;

    void main()
    {
        vec2 p = uv;
        vec4 acc = vec4(0.0);
        for (int i = 0; i < Iterations; i++)
        {
            vec4 texel = texture(Noise, p);
            acc += texel;
            p = fract(p + texel.xy * 0.173 + vec2(0.013, 0.029) * float(i));
        }
        target = acc / float(max(Iterations, 1)) * Tint;
    }
}
