Shader "Quill/CanvasShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        LOD 100

        Lighting Off
        Cull Off
        ZWrite On
        ZTest Always
        Blend One OneMinusSrcAlpha  // Premultiplied alpha (Quill uses premultiplied colors)

        Pass
        {
            Name "QUILL BUILTIN"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float2 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float2 fragPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4x4 _ScissorMat;
            float2 _ScissorExt;

            float4x4 _BrushMat;
            int _BrushType;
            float4 _BrushColor1;
            float4 _BrushColor2;
            float4 _BrushParams;
            float2 _BrushParams2;

            float4x4 _BrushTextureMat;
            float _DpiScale;

            float calculateBrushFactor(float2 pixelPos)
            {
                if (_BrushType == 0) return 0.0;

                float2 logicalPos = pixelPos / _DpiScale;
                float2 transformedPoint = mul(_BrushMat, float4(logicalPos, 0.0, 1.0)).xy;

                // Linear brush
                if (_BrushType == 1)
                {
                    float2 startPoint = _BrushParams.xy;
                    float2 endPoint = _BrushParams.zw;
                    float2 lineDir = endPoint - startPoint;
                    float lineLen = length(lineDir);

                    if (lineLen < 0.001) return 0.0;

                    float2 posToStart = transformedPoint - startPoint;
                    float projection = dot(posToStart, lineDir) / (lineLen * lineLen);
                    return saturate(projection);
                }

                // Radial brush
                if (_BrushType == 2)
                {
                    float2 center = _BrushParams.xy;
                    float innerRadius = _BrushParams.z;
                    float outerRadius = _BrushParams.w;

                    if (outerRadius < 0.001) return 0.0;

                    float2 toCenter = transformedPoint - center;
                    float distFromCenter = length(toCenter);
                    float t = smoothstep(innerRadius, outerRadius, distFromCenter);
                    return saturate(t);
                }

                // Box brush
                if (_BrushType == 3)
                {
                    float2 center = _BrushParams.xy;
                    float2 halfSize = _BrushParams.zw;
                    float cornerRadius = _BrushParams2.x;
                    float feather = _BrushParams2.y;

                    if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;

                    float2 q = abs(transformedPoint - center) - (halfSize - float2(cornerRadius, cornerRadius));
                    float2 qClamped = max(q, float2(0.0, 0.0));
                    float outsideDist = length(qClamped);
                    float insideDist = min(max(q.x, q.y), 0.0);
                    float dist = insideDist + outsideDist - cornerRadius;

                    return saturate((dist + feather * 0.5) / feather);
                }

                return 0.0;
            }

            float scissorMask(float2 pixelPos)
            {
                if (_ScissorExt.x < 0.0 || _ScissorExt.y < 0.0) return 1.0;

                float2 logicalP = pixelPos / _DpiScale;
                float2 transformedPoint = mul(_ScissorMat, float4(logicalP, 0.0, 1.0)).xy;
                float2 logicalExt = _ScissorExt / _DpiScale;
                float2 distanceFromEdges = abs(transformedPoint) - logicalExt;
                float halfPixelLogical = 0.5 / _DpiScale;
                float2 smoothEdges = float2(halfPixelLogical, halfPixelLogical) - distanceFromEdges;

                return saturate(smoothEdges.x) * saturate(smoothEdges.y);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(float4(v.vertex, 0.0, 1.0));
                o.uv = v.uv;
                o.color = v.color;
                o.fragPos = v.vertex;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float mask = scissorMask(i.fragPos);
                float4 color = i.color;

                // Apply brush if active
                if (_BrushType > 0)
                {
                    float factor = calculateBrushFactor(i.fragPos);
                    color = lerp(_BrushColor1, _BrushColor2, factor);
                }

                // Text mode: UV >= 2.0 means text rendering
                if (i.uv.x >= 2.0)
                {
                    return color * tex2D(_MainTex, i.uv - float2(2.0, 0.0)) * mask;
                }

                // Edge anti-aliasing
                float2 pixelSize = fwidth(i.uv);
                float2 edgeDistance = min(i.uv, 1.0 - i.uv);
                float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
                edgeAlpha = saturate(edgeAlpha);

                // Texture sampling
                float2 logicalPos = i.fragPos / _DpiScale;
                float2 texCoord = mul(_BrushTextureMat, float4(logicalPos, 0.0, 1.0)).xy;

                return color * tex2D(_MainTex, texCoord) * edgeAlpha * mask;
            }
            ENDCG
        }
    }
}
