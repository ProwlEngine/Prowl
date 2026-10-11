// Consumes four vertex attributes of differing sizes to exercise strides, offsets and locations.
Vertex
{
    layout(location = 0) in vec3 A_V3;
    layout(location = 1) in vec4 B_V4;
    layout(location = 2) in vec2 C_V2;
    layout(location = 3) in vec4 D_V4;

    void main()
    {
        vec4 sink = vec4(A_V3, 0) + B_V4 + vec4(C_V2, 0, 0) + D_V4;
        gl_Position = vec4(0, 0, 0, 1) + sink * 0.0;
    }
}

Fragment
{
    out vec4 outColor;

    void main()
    {
        outColor = vec4(0, 0, 0, 1);
    }
}
