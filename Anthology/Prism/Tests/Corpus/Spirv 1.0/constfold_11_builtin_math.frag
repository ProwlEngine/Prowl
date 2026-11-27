#version 460
layout(location=0) out vec4 fragColor;

void main() {
    float a = abs(-3.0);            // 3
    float b = min(2.0, 5.0);        // 2
    float c = max(2.0, 5.0);        // 5
    float d = clamp(9.0, 0.0, 4.0); // 4
    float e = floor(3.7);           // 3
    float f = ceil(3.2);            // 4
    float g = mod(7.0, 3.0);        // 1
    float h = pow(2.0, 3.0);        // 8
    float i = sqrt(9.0);            // 3
    float j = exp(0.0);             // 1
    float k = log(1.0);             // 0
    fragColor = vec4((a + b + c) / 10.0, (d + e + f) / 11.0, (g + h + i) / 12.0, (j + k + 1.0) / 2.0);
}
