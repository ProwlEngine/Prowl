// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Tests.Types;

public class ObjectWithNestedTypes
{
    public class NestedClass
    {
        public string Value = "Nested";
    }

    public class NestedInheritedClass : NestedClass
    {
        public string InheritedValue = "Inherited";
    }

    public NestedClass NestedA = new NestedClass();
    public NestedClass NestedB = new NestedInheritedClass();
}
