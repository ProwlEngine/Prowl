namespace Prowl.Echo;

/// <summary>
/// Implements LEB128 (Little Endian Base 128) encoding for variable-length integer encoding.
/// </summary>
public static class LEB128
{
    #region Writing

    /// <summary>
    /// Writes an unsigned integer to the stream using ULEB128 encoding.
    /// </summary>
    public static void WriteUnsigned(BinaryWriter writer, ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F); // Get 7 least significant bits
            value >>= 7;                    // Shift right by 7 bits

            // If there's more data to write, set the high bit
            if (value != 0)
                b |= 0x80;

            writer.Write(b);
        } while (value != 0);
    }

    /// <summary>
    /// Writes a signed integer to the stream using SLEB128 encoding.
    /// </summary>
    public static void WriteSigned(BinaryWriter writer, long value)
    {
        bool more;
        do
        {
            byte b = (byte)(value & 0x7F); // Get 7 least significant bits
            value >>= 7;                    // Arithmetic shift right by 7 bits

            // If value is negative, fill in the gaps with 1s
            if (value == -1 && (b & 0x40) != 0)
                more = false;
            // If value is positive and there might be more significant 1 bits
            else if (value == 0 && (b & 0x40) == 0)
                more = false;
            else
                more = true;

            if (more)
                b |= 0x80;

            writer.Write(b);
        } while (more);
    }

    #endregion

    #region Reading

    /// <summary>
    /// Reads an unsigned integer from the stream using ULEB128 encoding.
    /// </summary>
    public static ulong ReadUnsigned(BinaryReader reader)
    {
        ulong result = 0;
        int shift = 0;
        byte b;

        do
        {
            if (shift >= 64)
                throw new OverflowException("ULEB128 value is too large");

            b = reader.ReadByte();
            result |= ((ulong)(b & 0x7F) << shift);
            shift += 7;
        } while ((b & 0x80) != 0);

        return result;
    }

    /// <summary>
    /// Reads a signed integer from the stream using SLEB128 encoding.
    /// </summary>
    public static long ReadSigned(BinaryReader reader)
    {
        long result = 0;
        int shift = 0;
        byte b;

        do
        {
            if (shift >= 64)
                throw new OverflowException("SLEB128 value is too large");

            b = reader.ReadByte();
            result |= ((long)(b & 0x7F) << shift);
            shift += 7;
        } while ((b & 0x80) != 0);

        // Sign extend if necessary
        if ((shift < 64) && (b & 0x40) != 0)
            result |= -(1L << shift);

        return result;
    }

    #endregion
}
