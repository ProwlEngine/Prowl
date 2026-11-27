// Set 0 is per view and shared across passes, set 1 is per draw. The texture lives in set 1 so the descriptor write path is checked too.
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
    in vec2 uv;
    in vec4 color;

    out vec4 target;

    void main()
    {
        target = texture(MainTexture, uv) + color;
    }
}
