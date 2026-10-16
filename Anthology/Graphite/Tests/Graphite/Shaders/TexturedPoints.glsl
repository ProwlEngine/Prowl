// Renders points placed by an ortho matrix and colored by sampling the center of a 2D texture.
Vertex
{
    layout(set = 0, binding = 0, std140) uniform Resources { mat4 Ortho; };

    layout(location = 0) in vec2 Position;

    void main()
    {
        gl_Position = Ortho * vec4(Position, 0, 1);
        gl_PointSize = 1.0;
    }
}

Fragment
{
    layout(set = 0, binding = 1) uniform texture2D Tex;
    layout(set = 0, binding = 2) uniform sampler Smp;

    out vec4 outColor;

    void main()
    {
        outColor = texture(sampler2D(Tex, Smp), vec2(0.5, 0.5));
    }
}
