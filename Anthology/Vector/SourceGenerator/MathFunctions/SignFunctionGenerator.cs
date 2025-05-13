namespace SourceGenerator.MathFunctions
{
    [MathFunction("Sign")]
    public class SignFunctionGenerator : SimpleMathFunctionGenerator
    {
        public override string[] SupportedTypes => new[] { "float", "double", "int" };
        protected override string MathMethodName => "Sign";
    }
}