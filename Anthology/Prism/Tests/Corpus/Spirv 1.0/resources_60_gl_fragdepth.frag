#version 450
layout(location = 0) in float vDepthBias;
layout(location = 0) out vec4 oColor;
void main() {
    gl_FragDepth = gl_FragCoord.z + vDepthBias;
    oColor = vec4(vec3(gl_FragDepth), 1.0);
}
