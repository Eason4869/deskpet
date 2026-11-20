using System;
using System.IO;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class DocumentReadStream : FileStream
    {
        readonly CancellationToken token;

        internal DocumentReadStream(string path, CancellationToken token)
            : base(path, FileMode.Open, FileAccess.Read, FileShare.Read) { this.token = token; }

        public override int ReadByte()
        {
            token.ThrowIfCancellationRequested();
            return base.ReadByte();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            token.ThrowIfCancellationRequested();
            return base.Read(buffer, offset, count);
        }

#if PORTABLE
        public override int Read(Span<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            return base.Read(buffer);
        }
#endif
    }
}
