using System.Text.Json;
using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Reports;

public static partial class ExchangeReportEvidenceSchema
{
    /// <summary>Count actual writer bytes without materialising another whole encoded report or permitting a raised cap.</summary>
    internal static void RequireEncodedSize(ExchangeReportEvidence evidence, int maximumBytes)
    {
        if (maximumBytes is < 1 or > MaximumBytes) throw Bad("The encoded report reader budget cannot be disabled or raised.");
        using var counter = new EncodedBudgetStream(maximumBytes);
        JsonSerializer.Serialize(counter, evidence, Strict);
    }

    private sealed class EncodedBudgetStream(int maximumBytes) : Stream
    {
        private long _written;
        private bool _closed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_closed;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() => ObjectDisposedException.ThrowIf(_closed, this);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (buffer.Length > maximumBytes - _written)
                throw Bad("Encoded Exchange report evidence exceeds its reader budget. No complete record can be admitted; reduce the report scope.");
            _written += buffer.Length;
        }
        protected override void Dispose(bool disposing) { _closed = true; base.Dispose(disposing); }
    }
}
