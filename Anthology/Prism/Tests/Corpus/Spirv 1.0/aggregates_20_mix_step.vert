#version 460

layout(location = 0) in vec3 inA;
layout(location = 1) in vec3 inB;

void main() {
    bvec3 sel = greaterThan(inA, inB);
    vec3 m = mix(inA, inB, sel);
    vec3 s = step(vec3(0.5), inA);
    vec3 ss = smoothstep(vec3(0.0), vec3(1.0), inB);
    gl_Position = vec4(m + s + ss, 1.0);
}
