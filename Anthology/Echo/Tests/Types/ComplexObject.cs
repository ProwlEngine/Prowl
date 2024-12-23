// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Tests.Types;

public class ComplexObject
{
    public SimpleObject Object = new();
    public List<int> Numbers = new() { 1, 2, 3 };
    public Dictionary<string, float> Values = new() { { "one", 1.0f }, { "two", 2.0f } };
}
