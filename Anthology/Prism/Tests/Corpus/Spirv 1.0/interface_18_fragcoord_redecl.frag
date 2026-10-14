#version 460

layout(origin_upper_left, pixel_center_integer) in vec4 gl_FragCoord;

layout(location = 0) out vec4 color;

void main() {
    color = gl_FragCoord;
}
