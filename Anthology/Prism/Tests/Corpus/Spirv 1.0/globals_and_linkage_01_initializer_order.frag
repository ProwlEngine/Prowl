#version 460 core

// Every module-scope initializer becomes a store at the top of main, in source order, before the body
// runs. That includes constant ones, ones nothing reads, and ones that call a function or depend on an
// earlier global.

layout(std140, set = 0, binding = 0) uniform Camera
{
    mat4 view;
    mat4 proj;
} camera;

layout(location = 0) in vec3 vPos;
layout(location = 0) out vec4 FragColor;

float helper(float x) { return x * 2.0 + 1.0; }

float gConstant = 1.5;                       // constant initializer: still a store
vec2 gFromUniform = camera.proj[0].xy;       // reads a block, materializing it early
float gFromHelper = helper(0.25);            // calls a function: leaves a param temp behind in main
float gChained = gConstant * 2.0;            // reads an earlier global rather than folding it
mat4 gMatrix = camera.view * camera.proj;
float gUnread = helper(9.0);                 // nothing reads it, but the initializer still runs
vec3 gDefault;                               // no initializer: no store at all

void main()
{
    vec3 p = vPos * gChained + gDefault;
    FragColor = gMatrix * vec4(p, 1.0) + vec4(gFromUniform, gFromHelper, gConstant);
}
