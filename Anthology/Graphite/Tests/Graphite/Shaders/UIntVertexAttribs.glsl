// Renders points colored by uint vertex attributes divided by a uniform factor, placed by an ortho matrix.
Vertex
{
    layout(set = 0, binding = 0, std140) uniform Model
    {
        mat4 Ortho;
        uint ColorNormalizationFactor;
    };

    layout(location = 0) in vec2 Position;
    layout(location = 1) in uvec4 Color_Int;

    out vec4 vColor;

    void main()
    {
        gl_Position = Ortho * vec4(Position, 0, 1);
        gl_PointSize = 1.0;
        vColor = vec4(vec3(Color_Int.xyz), 1.0) / float(ColorNormalizationFactor);
        vColor.w = 1.0;
    }
}

Fragment
{
    in vec4 vColor;
    out vec4 outColor;

    void main()
    {
        outColor = vColor;
    }
}
