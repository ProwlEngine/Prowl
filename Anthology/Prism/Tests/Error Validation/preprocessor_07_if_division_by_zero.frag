// Error: preprocessor arithmetic divides by zero in an #if expression
#version 450

#if 1/0
void main() {}
#endif
