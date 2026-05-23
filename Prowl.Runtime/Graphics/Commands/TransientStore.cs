// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Prowl.Runtime;

/// <summary>
/// A per-CommandBuffer bump-pointer arena for "blob" payloads (matrix arrays, buffer
/// uploads, texture pixel data, debug labels). The encoder copies caller data into
/// the store so the user's source span/array can go away immediately; the executor
/// reads back out by offset+length.
///
/// Backing storage is rented from <see cref="ArrayPool{T}.Shared"/>. On reset the
/// store keeps one page and returns the rest to the pool, so a steady-state buffer
/// doesn't churn GC at all.
/// </summary>
internal sealed class TransientStore
{
    private const int InitialPageSize = 16 * 1024;

    // Pages are appended as we outgrow the current one. _activePage always points
    // at the page we're writing into; previous pages are sealed.
    private byte[][] _pages = new byte[4][];
    private int _pageCount;
    private int _activePage;
    private int _activeWritePos;
    private int _activePageSize;

    public TransientStore()
    {
        _pages[0] = ArrayPool<byte>.Shared.Rent(InitialPageSize);
        _activePageSize = _pages[0].Length;
        _pageCount = 1;
    }

    /// <summary>Encoded reference into the store. Pages are not contiguous, so the
    /// reader needs to know which page the data lives in.</summary>
    public readonly struct Ref
    {
        public readonly ushort Page;
        public readonly uint Offset;
        public readonly uint Length;

        public Ref(ushort page, uint offset, uint length)
        {
            Page = page;
            Offset = offset;
            Length = length;
        }
    }

    /// <summary>Copy <paramref name="data"/> into the store and return a handle the
    /// executor can use to read it back.</summary>
    public Ref Park(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return new Ref((ushort)_activePage, (uint)_activeWritePos, 0);

        EnsureRoom(data.Length);
        int offset = _activeWritePos;
        data.CopyTo(_pages[_activePage].AsSpan(offset, data.Length));
        _activeWritePos += data.Length;
        return new Ref((ushort)_activePage, (uint)offset, (uint)data.Length);
    }

    public Ref Park<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        return Park(MemoryMarshal.AsBytes(data));
    }

    public ReadOnlySpan<byte> Read(Ref r)
    {
        if (r.Length == 0) return ReadOnlySpan<byte>.Empty;
        return _pages[r.Page].AsSpan((int)r.Offset, (int)r.Length);
    }

    public ReadOnlySpan<T> Read<T>(Ref r) where T : unmanaged
    {
        return MemoryMarshal.Cast<byte, T>(Read(r));
    }

    /// <summary>Clear all writes and return everything but the first page to the pool.</summary>
    public void Reset()
    {
        // Return all extra pages.
        for (int i = 1; i < _pageCount; i++)
        {
            ArrayPool<byte>.Shared.Return(_pages[i]);
            _pages[i] = null!;
        }
        _pageCount = 1;
        _activePage = 0;
        _activeWritePos = 0;
        _activePageSize = _pages[0].Length;
    }

    /// <summary>Drop everything including page 0. Called when the owning buffer is
    /// being torn down for good (over-cap pool returns).</summary>
    public void Dispose()
    {
        for (int i = 0; i < _pageCount; i++)
        {
            if (_pages[i] != null)
            {
                ArrayPool<byte>.Shared.Return(_pages[i]);
                _pages[i] = null!;
            }
        }
        _pageCount = 0;
        _activePage = 0;
        _activeWritePos = 0;
        _activePageSize = 0;
    }

    private void EnsureRoom(int bytes)
    {
        if (_activeWritePos + bytes <= _activePageSize)
            return;

        // The data is too big for the current page's remainder. Start a fresh page
        // sized to fit, with some headroom.
        int newPageSize = Math.Max(_activePageSize * 2, bytes + 256);
        byte[] newPage = ArrayPool<byte>.Shared.Rent(newPageSize);

        if (_pageCount == _pages.Length)
            Array.Resize(ref _pages, _pages.Length * 2);

        _pages[_pageCount++] = newPage;
        _activePage = _pageCount - 1;
        _activeWritePos = 0;
        _activePageSize = newPage.Length;
    }
}
