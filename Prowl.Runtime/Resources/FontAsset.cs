// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Runtime.Audio;
using Prowl.Scribe;

namespace Prowl.Runtime.Resources;

public class FontAsset : EngineObject, ISerializable
{
    public string fontName;
    public byte[] fontData;

    private Prowl.Scribe.FontFile _scribeFont;

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        // Save the name
        compound.Add("Name", new EchoObject(fontName ?? string.Empty));

        compound.Add("Data", new EchoObject(fontData));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        // Restore the name
        fontName = value["Name"].StringValue;

        fontData = value["Data"].ByteArrayValue;
    }

    public Prowl.Scribe.FontFile GetScribeFont()
    {
        _scribeFont ??= new FontFile(fontData);
        return _scribeFont;
    }
}
