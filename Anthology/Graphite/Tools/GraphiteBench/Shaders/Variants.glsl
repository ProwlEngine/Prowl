Shared
{
    layout(set = 0) uniform View
    {
        mat4 MatrixViewProjection;
        vec4 ViewTint;
    };

    layout(set = 1) uniform Model
    {
        mat4 MatrixModel;
        vec4 Color;
    };

    layout(set = 1) uniform sampler2D MainTexture;
}

Vertex
{
    in vec3 POSITION0;
    in vec2 UV0;

    out vec2 uv;
    out vec4 color;

    void main()
    {
        vec4 world = MatrixModel * vec4(POSITION0, 1.0);
        gl_Position = MatrixViewProjection * world;
        uv = UV0;
        color = Color * ViewTint;
    }
}

Fragment
{
    #pragma variant A
    #pragma variant B
    #pragma variant C
    #pragma variant D
    #pragma variant E
    #pragma variant F

    in vec2 uv;
    in vec4 color;

    out vec4 target;

    void main()
    {
        vec4 c = texture(MainTexture, uv) * color;
    #ifdef A
        c.r = 1.0 - c.r;
    #endif
    #ifdef B
        c.g *= 0.5;
    #endif
    #ifdef C
        c.b = c.r * c.g;
    #endif
    #ifdef D
        c = c.bgra;
    #endif
    #ifdef E
        c.rgb = sqrt(c.rgb);
    #endif
    #ifdef F
        c.a = 0.5;
    #endif
        target = c;
    }
}
