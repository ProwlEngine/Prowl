// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Clay.Internal.IO;

/// <summary>
/// Parses RFC 2397 <c>data:</c> URIs, the form used by glTF for embedded buffers and images.
/// </summary>
/// <remarks>
/// Examples:
/// <code>
///   data:application/octet-stream;base64,AAAA...
///   data:image/png;base64,iVBORw0KG...
///   data:,Hello%2C%20World
/// </code>
/// Only base64-encoded payloads are commonly produced by glTF tooling. Plain (URL-encoded)
/// payloads are also supported for completeness.
/// </remarks>
internal static class DataUri
{
    /// <summary>
    /// Returns true and fills <paramref name="mimeType"/>/<paramref name="data"/> if <paramref name="uri"/>
    /// is a <c>data:</c> URI; otherwise returns false and leaves outputs at default.
    /// </summary>
    public static bool TryDecode(string uri, out string mimeType, out byte[] data)
    {
        mimeType = string.Empty;
        data = Array.Empty<byte>();

        if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        int comma = uri.IndexOf(',');
        if (comma < 0)
            return false;

        string meta = uri.Substring(5, comma - 5);
        string payload = uri[(comma + 1)..];

        bool base64 = false;
        if (meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            base64 = true;
            meta = meta[..^7];
        }

        mimeType = string.IsNullOrEmpty(meta) ? "text/plain" : meta;

        try
        {
            data = base64
                ? Convert.FromBase64String(payload)
                : PercentDecode(payload);
        }
        catch (FormatException ex)
        {
            throw new ImportException($"Malformed base64 in data: URI for {mimeType}.", inner: ex);
        }

        return true;
    }

    /// <summary>
    /// Decodes a percent-encoded payload into raw bytes. Each <c>%XX</c> becomes a single byte and
    /// every other character is taken as its literal byte value, so binary payloads survive intact.
    /// </summary>
    private static byte[] PercentDecode(string payload)
    {
        var bytes = new List<byte>(payload.Length);
        for (int i = 0; i < payload.Length; i++)
        {
            char c = payload[i];
            if (c == '%' && i + 2 < payload.Length
                && Uri.IsHexDigit(payload[i + 1]) && Uri.IsHexDigit(payload[i + 2]))
            {
                bytes.Add((byte)(Uri.FromHex(payload[i + 1]) * 16 + Uri.FromHex(payload[i + 2])));
                i += 2;
            }
            else
            {
                bytes.Add((byte)c);
            }
        }
        return bytes.ToArray();
    }
}
