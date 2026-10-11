// Copies every layer of a multiview scene colour to the matching view of its target with a fullscreen triangle.
Vertex
{
    #extension GL_EXT_multiview : require
    layout(location = 0) out vec2 vUV;

    void main()
    {
        vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
        vUV = p;
        gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
    }
}

Fragment
{
    #extension GL_EXT_multiview : require
    uniform sampler2DArray Graphite_Source;
    layout(location = 0) in vec2 vUV;
    layout(location = 0) out vec4 outColor;

    void main()
    {
        outColor = texture(Graphite_Source, vec3(vUV, float(gl_ViewIndex)));
    }
}
