// Pulls per vertex position and color from a read only structured buffer indexed by vertex id.
Vertex
{
    struct ColoredVertex
    {
        vec4 Color;
        vec2 Position;
        vec2 _padding0;
    };

    layout(set = 0, binding = 0, std430) readonly buffer InputVertices { ColoredVertex vertices[]; } inputVertices;

    out vec4 Color;

    void main()
    {
        ColoredVertex v = inputVertices.vertices[gl_VertexIndex];
        gl_Position = vec4(v.Position, 0, 1);
        Color = v.Color;
    }
}

Fragment
{
    in vec4 Color;
    out vec4 outColor;

    void main()
    {
        outColor = Color;
    }
}
