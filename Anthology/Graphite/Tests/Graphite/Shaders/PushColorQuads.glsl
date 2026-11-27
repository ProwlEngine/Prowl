// Fills the given triangles with the colour from the push constants.
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
    layout(push_constant) uniform Push { vec4 color; } push;

    out vec4 outColor;

    void main()
    {
        outColor = push.color;
    }
}
