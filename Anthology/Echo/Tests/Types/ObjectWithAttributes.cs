// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;

namespace Tests.Types;

public class ObjectWithAttributes
{
    [FormerlySerializedAs("oldName")]
    public string NewName = "Test";

    [IgnoreOnNull]
    public string? OptionalField = null;

    [SerializeField]
    private float[] privateField = null;
}
