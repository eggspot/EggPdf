using System;
using System.Collections.Generic;
using System.IO;

namespace EggPdf.Pdf;

internal abstract class PdfByteSource : IDisposable
{
    private const int SearchChunkBytes = 1024 * 1024;

    public abstract long Length { get; }

    public abstract bool IsInMemory { get; }

    public abstract PdfByteWindow Window(long start, int length);

    public abstract void CopyTo(Stream output);

    public virtual void Dispose()
    {
    }

    public static PdfByteSource Over(byte[] data) => new ArraySource(data);

    public static PdfByteSource Over(Stream stream, bool ownsStream) => new StreamSource(stream, ownsStream);

    protected abstract void Read(long position, byte[] buffer, int length);

    public long[] FindAll(string token, int maxHits)
    {
        var hits = new List<long>();
        int span = SearchChunkBytes + token.Length - 1;
        byte[]? buffer = IsInMemory ? null : new byte[(int)Math.Min(span, Math.Max(Length, 0))];
        for (long chunkStart = 0; chunkStart <= Length - token.Length; chunkStart += SearchChunkBytes)
        {
            int length = (int)Math.Min(span, Length - chunkStart);
            byte[] data;
            int offset;
            if (buffer == null)
            {
                var window = Window(chunkStart, length);
                data = window.Data;
                offset = window.Offset;
            }
            else
            {
                Read(chunkStart, buffer, length);
                data = buffer;
                offset = 0;
            }
            for (int at = PdfSyntaxParser.IndexOf(data, token, offset, offset + length);
                 at >= 0 && at - offset < SearchChunkBytes;
                 at = PdfSyntaxParser.IndexOf(data, token, at + 1, offset + length))
            {
                if (hits.Count >= maxHits) throw new PdfParseBudgetExceededException("The document repeats a keyword far too often to index.");
                hits.Add(chunkStart + (at - offset));
            }
        }
        return hits.ToArray();
    }

    public long LastIndexOf(string token)
    {
        int span = SearchChunkBytes + token.Length - 1;
        byte[]? buffer = IsInMemory ? null : new byte[(int)Math.Min(span, Math.Max(Length, 0))];
        for (long chunkEnd = Length; chunkEnd >= token.Length; chunkEnd -= SearchChunkBytes)
        {
            long chunkStart = Math.Max(0, chunkEnd - span);
            int length = (int)(chunkEnd - chunkStart);
            byte[] data;
            int offset;
            if (buffer == null)
            {
                var window = Window(chunkStart, length);
                data = window.Data;
                offset = window.Offset;
            }
            else
            {
                Read(chunkStart, buffer, length);
                data = buffer;
                offset = 0;
            }
            int found = PdfSyntaxParser.LastIndexOf(data, token, offset, offset + length);
            if (found >= 0) return chunkStart + (found - offset);
        }
        return -1;
    }

    private sealed class ArraySource : PdfByteSource
    {
        private readonly byte[] _data;

        public ArraySource(byte[] data) => _data = data;

        public override long Length => _data.Length;

        public override bool IsInMemory => true;

        public override PdfByteWindow Window(long start, int length) =>
            new(_data, (int)start, 0, (int)Math.Max(start, Math.Min(_data.Length, start + (long)length)));

        public override void CopyTo(Stream output) => output.Write(_data, 0, _data.Length);

        protected override void Read(long position, byte[] buffer, int length) => Array.Copy(_data, position, buffer, 0, length);
    }

    private sealed class StreamSource : PdfByteSource
    {
        private const int BlockBytes = 64 * 1024;
        private const int CachedBlocks = 16;
        private const int CopyBufferBytes = 80 * 1024;

        private readonly Stream _stream;
        private readonly bool _ownsStream;
        private readonly long _length;
        private readonly long[] _blockStarts = new long[CachedBlocks];
        private readonly byte[][] _blocks = new byte[CachedBlocks][];
        private readonly int[] _blockLengths = new int[CachedBlocks];
        private int _nextVictim;

        public StreamSource(Stream stream, bool ownsStream)
        {
            if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
            _stream = stream;
            _ownsStream = ownsStream;
            _length = stream.Length;
            for (int i = 0; i < CachedBlocks; i++) _blockStarts[i] = -1;
        }

        public override long Length => _length;

        public override bool IsInMemory => false;

        public override PdfByteWindow Window(long start, int length)
        {
            if (start < 0 || start >= _length) return new PdfByteWindow(Array.Empty<byte>(), 0, start, 0);
            length = (int)Math.Max(0, Math.Min(length, _length - start));
            long blockStart = start - start % BlockBytes;
            if (start + length <= blockStart + BlockBytes)
            {
                var block = Block(blockStart, out int blockLength);
                return new PdfByteWindow(block, (int)(start - blockStart), blockStart, Math.Min(blockLength, (int)(start - blockStart) + length));
            }
            var buffer = new byte[length];
            ReadExactly(start, buffer, length);
            return new PdfByteWindow(buffer, 0, start, length);
        }

        public override void CopyTo(Stream output)
        {
            var buffer = new byte[CopyBufferBytes];
            for (long copied = 0; copied < _length;)
            {
                int length = (int)Math.Min(buffer.Length, _length - copied);
                ReadExactly(copied, buffer, length);
                output.Write(buffer, 0, length);
                copied += length;
            }
        }

        protected override void Read(long position, byte[] buffer, int length) => ReadExactly(position, buffer, length);

        public override void Dispose()
        {
            if (_ownsStream) _stream.Dispose();
        }

        private byte[] Block(long blockStart, out int blockLength)
        {
            for (int i = 0; i < CachedBlocks; i++)
            {
                if (_blockStarts[i] != blockStart) continue;
                blockLength = _blockLengths[i];
                return _blocks[i];
            }
            int slot = _nextVictim;
            _nextVictim = (_nextVictim + 1) % CachedBlocks;
            var block = _blocks[slot] ??= new byte[BlockBytes];
            blockLength = (int)Math.Min(BlockBytes, _length - blockStart);
            ReadExactly(blockStart, block, blockLength);
            _blockStarts[slot] = blockStart;
            _blockLengths[slot] = blockLength;
            return block;
        }

        private void ReadExactly(long start, byte[] buffer, int length)
        {
            _stream.Position = start;
            int filled = 0;
            while (filled < length)
            {
                int read = _stream.Read(buffer, filled, length - filled);
                if (read <= 0) throw new PdfFormatException("The PDF stream ended before its reported length.");
                filled += read;
            }
        }
    }
}

internal readonly struct PdfByteWindow
{
    public PdfByteWindow(byte[] data, int offset, long baseOffset, int end = -1)
    {
        Data = data;
        Offset = offset;
        BaseOffset = baseOffset;
        End = end < 0 ? data.Length : end;
    }

    public byte[] Data { get; }
    public int Offset { get; }
    public long BaseOffset { get; }
    public int End { get; }
}
