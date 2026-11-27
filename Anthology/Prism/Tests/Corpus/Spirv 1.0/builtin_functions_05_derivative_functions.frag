#version 460 core

layout(location=0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float u_scale;
};

void main() {
    float v = gl_FragCoord.x * gl_FragCoord.y * u_scale;

    float r_dfdx = dFdx(v);
    float r_dfdy = dFdy(v);
    float r_fwidth = fwidth(v);

    float r_dfdx_coarse = dFdxCoarse(v);
    float r_dfdy_coarse = dFdyCoarse(v);
    float r_dfdx_fine = dFdxFine(v);
    float r_dfdy_fine = dFdyFine(v);

    float r_fwidth_coarse = fwidthCoarse(v);
    float r_fwidth_fine = fwidthFine(v);

    float total = r_dfdx + r_dfdy + r_fwidth + r_dfdx_coarse + r_dfdy_coarse +
                  r_dfdx_fine + r_dfdy_fine + r_fwidth_coarse + r_fwidth_fine;

    FragColor = vec4(total, r_dfdx, r_dfdy, 1.0);
}
