Shader "Hidden/XR/Mirror"
{
    Pass
    {
        Name "Mirror"
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

            // The part of the eye shown: offset in xy, size in zw, in UV
            uniform vec4 _Crop;

            in vec2 vUV;
            out vec4 FragColor;

            void main()
            {
                FragColor = vec4(texture(_MainTex, _Crop.xy + vUV * _Crop.zw).rgb, 1.0);
            }
        }
        ENDGLSL
    }
}
