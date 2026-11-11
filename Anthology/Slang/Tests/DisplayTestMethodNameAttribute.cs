// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Xunit.v3;


class DisplayTestMethodNameAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        Console.WriteLine($"Begin test '{methodUnderTest.Name}'");
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        Console.WriteLine($"Complete test '{methodUnderTest.Name}'");
    }
}
