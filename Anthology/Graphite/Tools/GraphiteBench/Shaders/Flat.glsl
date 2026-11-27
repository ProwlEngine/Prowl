Vertex
{
    in vec3 POSITION0;
    in vec2 UV0;

    void main()
    {
        gl_Position = vec4(POSITION0.xy, 0.0, 1.0);
    }
}

Fragment
{
    uniform vec4 Tint;

    out vec4 target;

    void main()
    {
        target = Tint;
    }
}
