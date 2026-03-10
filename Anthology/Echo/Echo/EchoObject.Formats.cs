// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

public sealed partial class EchoObject
{
    #region JSON

    /// <summary>
    /// Write this EchoObject to a JSON string.
    /// </summary>
    public string WriteToJson() => JsonFileFormat.Instance.WriteToString(this);

    /// <summary>
    /// Read an EchoObject from a JSON string. Supports generic JSON.
    /// </summary>
    public static EchoObject ReadFromJson(string json) => JsonFileFormat.Instance.ReadFromString(json);

    #endregion
}
