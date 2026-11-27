// Error: defined operator in an #if has no operand
#version 450

#if defined
void main() {}
#endif
