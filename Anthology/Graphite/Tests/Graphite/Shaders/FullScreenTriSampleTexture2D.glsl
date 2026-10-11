// Draws a full screen triangle without a vertex buffer and samples a 2D texture across it.
Vertex
{
    out vec2 UV;

    void main()
    {
        float x = -1.0 + float((gl_VertexIndex & 1) << 2);
        float y = -1.0 + float((gl_VertexIndex & 2) << 1);
        gl_Position = vec4(x, y, 0, 1);
        UV = vec2((x + 1.0) * 0.5, (y + 1.0) * 0.5);
    }
}

Fragment
{
    layout(set = 0, binding = 0) uniform texture2D Tex;
    layout(set = 0, binding = 1) uniform sampler Smp;

    in vec2 UV;
    out vec4 outColor;

    void main()
    {
        outColor = texture(sampler2D(Tex, Smp), UV);
    }
}
