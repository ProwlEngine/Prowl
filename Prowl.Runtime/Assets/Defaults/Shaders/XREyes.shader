Shader "Hidden/XR/Eyes"
{
    Pass
    {
        Name "Eyes"
        Cull Off
        ZTest Disabled
        ZWrite Off

        GLSLPROGRAM
        Vertex
        {
            out vec2 vUV;

            void main()
            {
                vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
                vUV = vec2(p.x, 1.0 - p.y);
                gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
            }
        }

        Fragment
        {
            uniform sampler2D _MainTex;

            in vec2 vUV;
            out vec4 FragColor;

            void main()
            {
                // The eye texture holds the display's rows bottom first (as Graphite presents views);
                // the compositor takes them top first, so this copy is the one place the flip happens.
                FragColor = vec4(texture(_MainTex, vec2(vUV.x, 1.0 - vUV.y)).rgb, 1.0);
            }
        }
        ENDGLSL
    }
}