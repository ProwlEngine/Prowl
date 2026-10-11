Shader "Sample/Lit"
{
    Pass
    {
        Name "Forward"

        Cull Off
        ZTest LessEqual
        ZWrite On

        GLSLPROGRAM
        Shared
        {
            uniform mat4 MatrixMVP;
            uniform mat4 MatrixModel;
            uniform vec4 Color;
            uniform vec4 LightDirection;
            uniform vec4 LightColor;
            uniform vec4 Ambient;
        }

        Vertex
        {
            in vec3 POSITION0;
            in vec3 NORMAL0;
            in vec2 TEXCOORD0;

            out vec3 normal;
            out vec2 uv;

            void main()
            {
                gl_Position = MatrixMVP * vec4(POSITION0, 1.0);
                normal = (MatrixModel * vec4(NORMAL0, 0.0)).xyz;
                uv = TEXCOORD0;
            }
        }

        Fragment
        {
            in vec3 normal;
            in vec2 uv;

            out vec4 target;

            void main()
            {
                vec3 n = normalize(normal);
                float diffuse = clamp(dot(n, -LightDirection.xyz), 0.0, 1.0);
                vec2 edge = abs(uv - 0.5) * 2.0;
                float rim = smoothstep(0.85, 1.0, max(edge.x, edge.y));
                vec3 albedo = Color.rgb * (1.0 - rim * 0.35);
                vec3 lit = albedo * (Ambient.rgb + LightColor.rgb * diffuse);
                target = vec4(lit, 1.0);
            }
        }
        ENDGLSL
    }
}
