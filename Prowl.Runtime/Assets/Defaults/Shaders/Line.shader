// LineRenderer ribbons. Each vertex holds its line point, the line direction in the tangent's xyz and its signed half
// width in the tangent's w, and is pushed sideways here so the ribbon faces whichever view draws it.
Shader "Default/Line"
{
    Properties
    {
        _MainTex("Texture", Texture2D) = "white"
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
        Cull Off
        ZWrite Off
        Blend SourceAlpha InverseSourceAlpha
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"

            out vec2 vUV;
            out vec3 vWorldPos;
            out vec4 vColor;

            void main()
            {
                vec3 point = vertexPosition;
                vec4 tangent = vertexTangent;
                vec3 center = Graphite_ObjectToWorld(point);
                vec3 direction = normalize(Graphite_ObjectToWorldDirection(tangent.xyz));

                // The camera sits at the origin of camera relative space
                vec3 right = cross(normalize(-center), direction);
                if (dot(right, right) < 0.001)
                {
                    mat4 view = Graphite_View().view;
                    right = cross(vec3(view[0][1], view[1][1], view[2][1]), direction);
                }
                vec3 offset = normalize(right) * tangent.w;

                vec3 world = center + offset;
                gl_Position = Graphite_RelativeToClip(world);
                Graphite_SetPreviousPosition(point + inverse(Graphite_Linear(Graphite_ObjectTransform())) * offset);

                vUV = vertexUV0;
                vWorldPos = world;
                vColor = vertexColor;
            }
        }
        Fragment
        {
            #include "Prowl/Common.glsl"
            #include "Graphite/Lighting.glsl"

            uniform sampler2D _MainTex;

            in vec2 vUV;
            in vec3 vWorldPos;
            in vec4 vColor;

            layout(location = 0) out vec4 fragColor;

            void main()
            {
                vec4 texel = texture(_MainTex, vUV);
                vec3 color = ApplyFog(texel.rgb * vColor.rgb, vWorldPos);
                fragColor = vec4(color, texel.a * vColor.a);
            }
        }
        ENDGLSL
    }
}
