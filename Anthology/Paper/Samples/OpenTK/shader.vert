// Attributes
in vec2 vertex;
in vec2 tcoord;

// Uniforms
uniform mat4 transformMat;

// Varyings
out vec2 fpos;
out vec2 ftcoord;

void main()
{
	ftcoord = tcoord;
	fpos = vertex;
	gl_Position = transformMat * vec4(vertex, 0, 1);
}
