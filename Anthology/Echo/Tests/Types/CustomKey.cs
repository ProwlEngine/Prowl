// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Tests.Types;

public class CustomKey
{
    public int Id;
    public string Name = "";

    public override bool Equals(object? obj)
    {
        if (obj is CustomKey other)
            return Id == other.Id && Name == other.Name;
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }
}
