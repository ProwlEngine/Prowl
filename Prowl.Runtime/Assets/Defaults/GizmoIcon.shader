Shader "Default/GizmoIcon"

Properties
{
    _MainTex ("Icon", Texture2D) = "white"
    _IconColor ("Color", Vector4) = (1.0, 1.0, 1.0, 1.0)
    _IconCenter ("Center", Vector3) = (0.0, 0.0, 0.0)
    _IconScale ("Scale", Float) = 1.0
}

Pass "GizmoIcon"
{
    Tags { "RenderOrder" = "Transparent" }

    Cull Off
    Blend Alpha
    ZWrite Off
    ZTest Always

    GLSLPROGRAM
    Vertex
    {
        #include "ProwlCG"
        #include "VertexAttributes"

        out vec2 vUV;

        uniform vec3 _IconCenter;
        uniform float _IconScale;

        void main()
        {
            // World-space billboard: 1 meter * _IconScale, always facing camera
            float halfSize = _IconScale * 0.5;

            // Camera right/up from the view matrix
            vec3 camRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);
            vec3 camUp    = vec3(PROWL_MATRIX_V[0][1], PROWL_MATRIX_V[1][1], PROWL_MATRIX_V[2][1]);

            // vertexPosition is -1..1 from fullscreen quad
            vec3 worldPos = _IconCenter
                + camRight * (vertexPosition.x * halfSize)
                + camUp    * (vertexPosition.y * halfSize);

            gl_Position = PROWL_MATRIX_VP * vec4(worldPos, 1.0);
            vUV = vertexPosition.xy * 0.5 + 0.5;
        }
    }
    Fragment
    {
        #include "ProwlCG"

        in vec2 vUV;
        layout (location = 0) out vec4 finalColor;

        uniform sampler2D _MainTex;
        uniform vec4 _IconColor;
        uniform sampler2D _CameraDepthTexture;

        void main()
        {
            vec4 texColor = texture(_MainTex, vUV);
            vec4 color = texColor * _IconColor;

            if (color.a < 0.01) discard;

            // Depth-based dimming (same as gizmo shader)
            vec2 screenUV = gl_FragCoord.xy / _ScreenParams.xy;
            float sceneDepth = texture(_CameraDepthTexture, screenUV).r;
            float fragmentDepth = gl_FragCoord.z;
            float occluded = step(sceneDepth, fragmentDepth - 0.00001);
            if (occluded > 0.5)
            {
                color.rgb *= 0.5;
                color.a *= 0.3;
            }

            finalColor = color;
        }
    }
    ENDGLSL
}
