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
    uniform sampler2D Input;

    in vec2 uv;

    out vec4 target;

    void main()
    {
        target = texture(Input, uv) * 0.5 + Tint;
    }
}
