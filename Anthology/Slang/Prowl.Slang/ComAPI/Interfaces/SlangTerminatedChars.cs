namespace Prowl.Slang.NativeAPI;


/* Can be requested from ISlangCastable cast to indicate the contained chars are null
 * terminated.
 */
[UUID(0xbe0db1a8, 0x3594, 0x4603, 0xa7, 0x8b, 0xc4, 0x86, 0x84, 0x30, 0xdf, 0xbb)]
interface SlangTerminatedChars
{
    //      operator byte*() const { return chars; }
    //      char chars[1];
};