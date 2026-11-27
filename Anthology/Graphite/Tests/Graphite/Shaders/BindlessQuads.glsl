// Fills the given triangles with the texture the push constant index picks from a bindless table, times a tint buffer.
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
    #extension GL_EXT_nonuniform_qualifier : require

    layout(set = 0, binding = 0) uniform texture2D Textures[4];
    layout(set = 0, binding = 1) uniform sampler Point;
    layout(set = 0, binding = 2, std430) readonly buffer Tints { vec4 tint; } tints;
    layout(push_constant) uniform Push { uint index; } push;

    out vec4 outColor;

    void main()
    {
        outColor = texture(sampler2D(Textures[nonuniformEXT(push.index)], Point), vec2(0.5)) * tints.tint;
    }
}
