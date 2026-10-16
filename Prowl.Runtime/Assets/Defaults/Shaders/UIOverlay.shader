Shader "Hidden/UIOverlay"
{
    Pass
    {
        Name "Overlay"
        Cull Off
        ZTest Disabled
        ZWrite Off
        Blend SourceAlpha InverseSourceAlpha

        GLSLPROGRAM
        Vertex
        {
            in vec3 aPosition;
            in vec2 aTexCoord;
            in vec4 aColor;

            // Element space to screen pixels, origin bottom left, and screen pixels to the clip mask's space
            uniform mat4 _Model;
            uniform mat4 _ClipToLocal;
            uniform vec2 _ScreenSize;

            out vec2 vUV;
            out vec4 vColor;
            out vec2 vClip;

            void main()
            {
                vec4 screen = _Model * vec4(aPosition, 1.0);
                vUV = aTexCoord;
                vColor = aColor;
                vClip = (_ClipToLocal * screen).xy;
                gl_Position = vec4(screen.xy / _ScreenSize * 2.0 - 1.0, 0.5, 1.0);
            }
        }

        Fragment
        {
            #include "Prowl/Common.glsl"
            #include "Prowl/UICore.glsl"

            in vec2 vUV;
            in vec4 vColor;
            in vec2 vClip;

            layout(location = 0) out vec4 finalColor;

            uniform sampler2D _MainTex;
            uniform vec4 _ClipRect;
            uniform vec4 _ClipParams;
            uniform int _Text;
            uniform int _Linear;

            void main()
            {
                vec4 color = vColor;
                if (_Text != 0)
                    color.a *= Prowl_SdfCoverage(texture(_MainTex, vUV).r, vUV, vec2(textureSize(_MainTex, 0)));
                else
                    color *= texture(_MainTex, vUV);
                float clip = Prowl_UIClip(vClip, _ClipRect, _ClipParams.x, _ClipParams.y);
                color.a *= _ClipParams.z > 0.5 ? clip : 1.0;
                if (_Linear != 0)
                    color.rgb = gammaToLinearSpace(color.rgb);
                finalColor = color;
            }
        }
        ENDGLSL
    }
}
