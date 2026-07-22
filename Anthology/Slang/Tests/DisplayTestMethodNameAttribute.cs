// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Xunit.Sdk;


class DisplayTestMethodNameAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest)
    {
        Console.WriteLine($"Begin test '{methodUnderTest.Name}'");
    }

    public override void After(MethodInfo methodUnderTest)
    {
        Console.WriteLine($"Complete test '{methodUnderTest.Name}'");
    }
}
