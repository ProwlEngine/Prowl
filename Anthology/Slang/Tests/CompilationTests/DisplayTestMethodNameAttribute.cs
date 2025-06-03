using System.Reflection;

using Xunit.Sdk;


class DisplayTestMethodNameAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest)
    {
        Console.WriteLine($"Beginning test '{methodUnderTest.Name}'");
    }

    public override void After(MethodInfo methodUnderTest)
    {
        Console.WriteLine($"Completed test '{methodUnderTest.Name}'");
    }
}
