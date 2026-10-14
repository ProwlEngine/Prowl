// A sampler descriptor declared as an array of arrays with an implicitly-sized INNER dimension. Only the
// outermost dimension of an array of arrays may be implicitly sized, so glslang rejects the declaration.
#version 460
layout(set = 0, binding = 0) uniform sampler2D textures[][];
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 color;
void main() { color = texture(textures[0][1], uv); }
