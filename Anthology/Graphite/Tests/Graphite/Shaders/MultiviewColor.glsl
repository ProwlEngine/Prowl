// A fullscreen triangle coloured by view: red for view 0, green for view 1, blue for any other.
Vertex
{
    void main()
    {
        vec2 uv = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
        gl_Position = vec4(uv * 2.0 - 1.0, 0, 1);
    }
}

Fragment
{
    #extension GL_EXT_multiview : require

    out vec4 outColor;

    void main()
    {
        outColor = gl_ViewIndex == 0 ? vec4(1, 0, 0, 1) : gl_ViewIndex == 1 ? vec4(0, 1, 0, 1) : vec4(0, 0, 1, 1);
    }
}
