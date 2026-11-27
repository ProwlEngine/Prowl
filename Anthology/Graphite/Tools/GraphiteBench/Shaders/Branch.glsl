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

    uint hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    void main()
    {
        uvec2 pixel = uvec2(gl_FragCoord.xy);
        uint h = hash(pixel.x * 7919u + pixel.y);
        float v = float(h & 0xFFu) / 255.0;
        vec3 acc = vec3(0.0);

        for (int i = 0; i < Iterations; i++)
        {
            h = hash(h + uint(i));
            switch (h & 3u)
            {
            case 0u:
                acc.x += sin(v * float(i));
                break;
            case 1u:
                for (int j = 0; j < 4; j++)
                    acc.y += cos(v + float(j) * 0.5);
                break;
            case 2u:
                if ((h & 16u) != 0u)
                    acc.z += exp(-v * float(i) * 0.01);
                else
                    acc.z -= sqrt(v + float(i));
                break;
            default:
                acc += vec3(v, v * v, v * v * v);
                break;
            }
        }
        target = vec4(fract(acc * 0.01), 1.0) * Tint;
    }
}
