namespace Prowl.Slang.Native;


/** A stream typically of text, used for outputting diagnostic as well as other information.
 */
[UUID(0xec457f0e, 0x9add, 0x4e6b, 0x85, 0x1c, 0xd7, 0xfa, 0x71, 0x6d, 0x15, 0xfd)]
public unsafe interface ISlangWriter : IUnknown
{
    /** Begin an append buffer.
    NOTE! Only one append buffer can be active at any time.
    @param maxNumChars The maximum of chars that will be appended
    @returns The start of the buffer for appending to. */
    byte* BeginAppendBuffer(nuint maxNumChars);

    /** Ends the append buffer, and is equivalent to a write of the append buffer.
    NOTE! That an endAppendBuffer is not necessary if there are no characters to write.
    @param buffer is the start of the data to append and must be identical to last value
    returned from beginAppendBuffer
    @param numChars must be a value less than or equal to what was returned from last call to
    beginAppendBuffer
    @returns Result, will be SLANG_OK on success */
    SlangResult EndAppendBuffer(byte* buffer, nuint numChars);

    /** Write text to the writer
    @param chars The characters to write out
    @param numChars The amount of characters
    @returns SLANG_OK on success */
    SlangResult Write(ConstU8Str chars, nuint numChars);

    /** Flushes any content to the output */
    void Flush();

    /** Determines if the writer stream is to the console, and can be used to alter the output
    @returns Returns true if is a console writer */
    CBool IsConsole();

    /** Set the mode for the writer to use
    @param mode The mode to use
    @returns SLANG_OK on success */
    SlangResult SetMode(SlangWriterMode mode);
}
