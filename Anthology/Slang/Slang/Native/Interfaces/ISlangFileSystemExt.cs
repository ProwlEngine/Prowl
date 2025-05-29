// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Slang.Native;


/** An extended file system abstraction.

Implementing and using this interface over ISlangFileSystem gives much more control over how
paths are managed, as well as how it is determined if two files 'are the same'.

All paths as input byte*, or output as ISlangBlobs are always encoded as UTF-8 strings.
Blobs that contain strings are always zero terminated.
*/
[UUID(0x5fb632d2, 0x979d, 0x4481, 0x9f, 0xee, 0x66, 0x3c, 0x3f, 0x14, 0x49, 0xe1)]
internal unsafe interface ISlangFileSystemExt : ISlangFileSystem
{
    /** Get a uniqueIdentity which uniquely identifies an object of the file system.

    Given a path, returns a 'uniqueIdentity' which ideally is the same value for the same object
    on the file system.

    The uniqueIdentity is used to compare if two paths are the same - which amongst other things
    allows Slang to cache source contents internally. It is also used for #pragma once
    functionality.

    A *requirement* is for any implementation is that two paths can only return the same
    uniqueIdentity if the contents of the two files are *identical*. If an implementation breaks
    this constraint it can produce incorrect compilation. If an implementation cannot *strictly*
    identify *the same* files, this will only have an effect on #pragma once behavior.

    The string for the uniqueIdentity is held zero terminated in the ISlangBlob of
    outUniqueIdentity.

    Note that there are many ways a uniqueIdentity may be generated for a file. For example it
    could be the 'canonical path' - assuming it is available and unambiguous for a file system.
    Another possible mechanism could be to store the filename combined with the file date time
    to uniquely identify it.

    The client must ensure the blob be released when no longer used, otherwise memory will leak.

    NOTE! Ideally this method would be called 'getPathUniqueIdentity' but for historical reasons
    and backward compatibility it's name remains with 'File' even though an implementation
    should be made to work with directories too.

    @param path
    @param outUniqueIdentity
    @returns A `SlangResult` to indicate success or failure getting the uniqueIdentity.
    */
    SlangResult GetFileUniqueIdentity(ConstU8Str path, out ISlangBlob* outUniqueIdentity);

    /** Calculate a path combining the 'fromPath' with 'path'

    The client must ensure the blob be released when no longer used, otherwise memory will leak.

    @param fromPathType How to interpret the from path - as a file or a directory.
    @param fromPath The from path.
    @param path Path to be determined relative to the fromPath
    @param pathOut Holds the string which is the relative path. The string is held in the blob
    zero terminated.
    @returns A `SlangResult` to indicate success or failure in loading the file.
    */
    SlangResult CalcCombinedPath(
        SlangPathType fromPathType,
        ConstU8Str fromPath,
        ConstU8Str path,
        out ISlangBlob* pathOut);

    /** Gets the type of path that path is on the file system.
    @param path
    @param pathTypeOut
    @returns SLANG_OK if located and type is known, else an error. SLANG_E_NOT_FOUND if not
    found.
    */
    SlangResult GetPathType(ConstU8Str path, out SlangPathType pathTypeOut);

    /** Get a path based on the kind.

    @param kind The kind of path wanted
    @param path The input path
    @param outPath The output path held in a blob
    @returns SLANG_OK if successfully simplified the path (SLANG_E_NOT_IMPLEMENTED if not
    implemented, or some other error code)
    */
    SlangResult GetPath(PathKind kind, ConstU8Str path, out ISlangBlob* outPath);

    /** Clears any cached information */
    void ClearCache();

    /** Enumerate the contents of the path

    Note that for normal Slang operation it isn't necessary to enumerate contents this can
    return SLANG_E_NOT_IMPLEMENTED.

    @param The path to enumerate
    @param callback This callback is called for each entry in the path.
    @param userData This is passed to the callback
    @returns SLANG_OK if successful
    */
    SlangResult EnumeratePathContents(ConstU8Str path, void* callback, void* userData);

    /** Returns how paths map to the OS file system

    @returns OSPathKind that describes how paths map to the Operating System file system
    */
    OSPathKind GetOSPathKind();
}
