#version 460 core

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    float _TResponse;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;                     // current-frame AO
layout(set = 2, binding = 2) uniform sampler2D _PreviousBuffer;              // accumulated AO history
layout(set = 2, binding = 3) uniform sampler2D _CameraMotionVectorsTexture;  // .rg motion

layout(location = 1) in vec2 TexCoords;

layout(location = 0) out vec4 fragColor;

void main()
{
    float current = texture(_MainTex, TexCoords).r;

    vec2 velocity = texture(_CameraMotionVectorsTexture, TexCoords).rg;
    vec2 prevUV = TexCoords - velocity;

    // Neighbourhood clamp: bound the history to the 3x3 range of the current AO so it can't
    // ghost where geometry/occlusion changed.
    vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0));
    float mn = current, mx = current;
    for (int x = -1; x <= 1; x++)
    for (int y = -1; y <= 1; y++)
    {
        float s = texture(_MainTex, TexCoords + texel * vec2(float(x), float(y))).r;
        mn = min(mn, s); mx = max(mx, s);
    }

    float previous = clamp(texture(_PreviousBuffer, prevUV).r, mn, mx);

    // Drop history on disocclusion (reprojected off-screen).
    float response = (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) ? 0.0 : _TResponse;

    float ao = mix(current, previous, response);
    fragColor = vec4(vec3(ao), 1.0);
}
