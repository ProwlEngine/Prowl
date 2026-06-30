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

    #region BSON

    /// <summary>
    /// Write this EchoObject to a BSON byte array.
    /// </summary>
    public byte[] WriteToBson() => BsonFileFormat.Instance.WriteToBytes(this);

    /// <summary>
    /// Read an EchoObject from a BSON byte array.
    /// </summary>
    public static EchoObject ReadFromBson(byte[] bson) => BsonFileFormat.Instance.ReadFromBytes(bson);

    #endregion

    #region YAML

    /// <summary>
    /// Write this EchoObject to a YAML string.
    /// </summary>
    public string WriteToYaml() => YamlFileFormat.Instance.WriteToString(this);

    /// <summary>
    /// Read an EchoObject from a YAML string. Supports generic YAML.
    /// </summary>
    public static EchoObject ReadFromYaml(string yaml) => YamlFileFormat.Instance.ReadFromString(yaml);

    #endregion

    #region XML

    /// <summary>
    /// Write this EchoObject to an XML string.
    /// </summary>
    public string WriteToXml() => XmlFileFormat.Instance.WriteToString(this);

    /// <summary>
    /// Read an EchoObject from an XML string. Supports generic XML.
    /// </summary>
    public static EchoObject ReadFromXml(string xml) => XmlFileFormat.Instance.ReadFromString(xml);

    #endregion
}
