// Gizmo lines, triangles and icons, drawn over the scene: full colour where visible, dimmed where something hides them.
#pragma once

#ifdef GRAPHITE_VERTEX
#include "Graphite/Vertex.glsl"
#endif

uniform sampler2D _MainTex;
uniform vec4 _Color;

#ifdef GRAPHITE_VERTEX
out vec4 vColor;
out vec2 vUV;

void main()
{
#ifdef GIZMO_ICON
    // A camera facing quad around the object's origin, as big as the object's scale
    mat4 view = Graphite_View().view;
    vec3 right = vec3(view[0][0], view[1][0], view[2][0]);
    vec3 up = vec3(view[0][1], view[1][1], view[2][1]);
    vec3 center = Graphite_ObjectToWorld(vec3(0.0));
    float scale = length(Graphite_ObjectToWorldDirection(vec3(1.0, 0.0, 0.0)));
    vec3 world = center + (right * vertexPosition.x + up * vertexPosition.y) * scale;
    gl_Position = Graphite_RelativeToClip(world);
    vColor = _Color;
    vUV = vec2(vertexUV0.x, 1.0 - vertexUV0.y);
#else
    gl_Position = Graphite_ObjectToClip(vertexPosition);
    vColor = vertexColor;
    vUV = vec2(0.0);
#endif
}
#else
in vec4 vColor;
in vec2 vUV;
layout(location = 0) out vec4 finalColor;

void main()
{
    vec4 color = vColor;
#ifdef GIZMO_ICON
    color *= texture(_MainTex, vUV);
    if (color.a < 0.01)
        discard;
#endif
#ifdef GIZMO_OCCLUDED
    color.rgb *= 0.5;
    color.a *= 0.3;
#endif
    finalColor = color;
}
#endif
