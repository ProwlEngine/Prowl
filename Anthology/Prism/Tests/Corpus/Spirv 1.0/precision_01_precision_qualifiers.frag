#version 460 core

precision highp float;

layout(location=0) out vec4 FragColor;

void main()
{
    highp float h = 1.0;
    mediump float m = 0.5;
    lowp float l = 0.25;

    FragColor = vec4(h * 0.25, m, l, 1.0);
}
