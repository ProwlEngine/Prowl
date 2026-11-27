#version 460
layout(location=0) out vec4 fragColor;

void main() {
    float a = clamp(0.5, 0.0, 1.0);       // 0.5
    float b = mix(0.0, 1.0, 0.25);        // 0.25
    float c = step(0.5, 0.75);            // 1.0
    float d = smoothstep(0.0, 1.0, 0.5);  // 0.5
    fragColor = vec4(a, b, c, d);
}
