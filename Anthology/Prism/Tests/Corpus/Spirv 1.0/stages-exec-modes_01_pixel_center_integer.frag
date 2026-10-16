#version 460
layout(pixel_center_integer) in vec4 gl_FragCoord;
layout(location = 0) out vec4 oColor;
void main()
{
    vec2 checker = floor(gl_FragCoord.xy / 16.0);
    float c = mod(checker.x + checker.y, 2.0);
    oColor = vec4(vec3(c), 1.0);
}
