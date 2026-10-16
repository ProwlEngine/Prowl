#version 460 core

// Every one of these built-ins is applied to constants only, so glslang's front end folds them in the
// AST and emits plain constants rather than any GLSL.std.450 call.

layout(location = 0) out vec4 FragColor;

const float A = 0.75;
const float B = 0.25;

void main()
{
    float p = pow(A, 3.0);
    float mn = min(A, B);
    float mx = max(A, B);
    float cl = clamp(1.5, A, B + 0.5);
    float mixed = mix(A, B, 0.25);
    float st = step(B, A);
    float ss = smoothstep(0.0, 1.0, A);
    float ab = abs(-A);
    float sg = sign(-A);
    float md = mod(1.75, 0.5);

    float total = p + mn + mx + cl + mixed + st + ss + ab + sg + md;
    FragColor = vec4(total, total * 0.5, total * 0.25, 1.0);
}
