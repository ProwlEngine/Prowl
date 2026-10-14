#version 460

#define SQR(x) ((x) * (x))
#define LEN2(a, b) (SQR(a) + SQR(b))

layout(location = 0) out vec4 fragColor;

void main() {
    float d = LEN2(0.3, 0.4);
    fragColor = vec4(d, SQR(0.5), 0.0, 1.0);
}
