#ifndef SHADER_PROWL
#define SHADER_PROWL
		
float LinearizeDepth(float depth, float near, float far) 
{
	float z = depth * 2.0 - 1.0; // Back to NDC [-1,1] range
	return (2.0 * near * far) / (far + near - z * (far - near));
}

#endif
