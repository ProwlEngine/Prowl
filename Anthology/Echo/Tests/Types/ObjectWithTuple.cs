// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Tests.Types;

public class ObjectWithTuple
{
    public (int, string) SimpleTuple = (1, "One");
    public ValueTuple<int, string, float> NamedTuple = (1, "One", 1.0f);
}
