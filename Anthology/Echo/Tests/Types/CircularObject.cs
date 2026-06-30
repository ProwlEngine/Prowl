// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Tests.Types;

public class CircularObject
{
    public string Name = "Parent";
    public CircularObject? Child;
}
