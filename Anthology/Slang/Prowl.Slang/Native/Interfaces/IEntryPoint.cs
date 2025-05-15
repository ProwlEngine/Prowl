namespace Prowl.Slang.NativeAPI;


[UUID(0x8f241361, 0xf5bd, 0x4ca0, 0xa3, 0xac, 0x2, 0xf7, 0xfa, 0x24, 0x2, 0xb8)]
public interface IEntryPoint : IComponentType
{
    /* FunctionReflection */
    void GetFunctionReflection();
}
