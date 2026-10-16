#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;
layout(set=0, binding=1) uniform sampler2DArray texArr;
layout(set=0, binding=2) uniform samplerCube texCube;

layout(location=0) out vec4 fragColor;

void main() {
    int lod = int(gl_FragCoord.x) & 3;
    ivec2 s2 = textureSize(tex2d, lod);
    ivec3 sa = textureSize(texArr, lod);
    ivec2 sc = textureSize(texCube, lod);
    fragColor = vec4(float(s2.x + s2.y), float(sa.z), float(sc.x), 1.0);
}
