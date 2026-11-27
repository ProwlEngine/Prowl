// Fills whatever triangles it is given with white.
Vertex
{
    layout(location = 0) in vec2 POSITION;

    void main()
    {
        gl_Position = vec4(POSITION, 0, 1);
    }
}

Fragment
{
    out vec4 outColor;

    void main()
    {
        outColor = vec4(1);
    }
}
