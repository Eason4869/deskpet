using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Dafeiyu
{
    // PdfPig does not bound decompression of individual streams by default.
    internal sealed class PdfStreamFilters : IFilterProvider
    {
        const int MaxDecodedStream = 16 * 1024 * 1024;
        const int MaxDecodedDocument = 96 * 1024 * 1024;
        readonly CancellationToken token;
        readonly IFilterProvider defaults = DefaultFilterProvider.Instance;
        int decodedDocumentBytes;

        internal PdfStreamFilters(CancellationToken token) { this.token = token; }

        public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary)
        { return Wrap(defaults.GetFilters(dictionary)); }

        public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names)
        { return Wrap(defaults.GetNamedFilters(names)); }

        public IReadOnlyList<IFilter> GetAllFilters()
        { return Wrap(defaults.GetAllFilters()); }

        IReadOnlyList<IFilter> Wrap(IReadOnlyList<IFilter> filters)
        {
            token.ThrowIfCancellationRequested();
            return filters.Select(filter => (IFilter)new LimitedFilter(filter, this)).ToArray();
        }

        void Charge(int bytes)
        {
            token.ThrowIfCancellationRequested();
            if (bytes > MaxDecodedStream || bytes > MaxDecodedDocument - decodedDocumentBytes)
                throw new PdfDocumentFormatException("PDF stream expansion budget exceeded.");
            decodedDocumentBytes += bytes;
        }

        sealed class LimitedFilter : IFilter
        {
            readonly IFilter inner;
            readonly PdfStreamFilters owner;

            internal LimitedFilter(IFilter inner, PdfStreamFilters owner)
            { this.inner = inner; this.owner = owner; }

            public bool IsSupported
            {
                get
                {
                    return inner is FlateFilter || inner is RunLengthFilter || inner is LzwFilter ||
                        inner is Ascii85Filter || inner is AsciiHexDecodeFilter;
                }
            }

            public Memory<byte> Decode(Memory<byte> input, DictionaryToken dictionary, IFilterProvider provider, int index)
            {
                owner.token.ThrowIfCancellationRequested();
                if (!IsSupported) throw new NotSupportedException("PDF image stream decoding is unavailable in the text reader.");
                if (input.Length > MaxDecodedStream || (inner is RunLengthFilter && input.Length > 250000) ||
                    (inner is LzwFilter && input.Length > 4096))
                    throw new PdfDocumentFormatException("PDF compressed stream exceeds the parsing limit.");

                var parameters = DecodeParameterResolver.GetFilterParameters(dictionary, index);
                int predictor = Parameter(parameters, NameToken.Predictor, 1);
                if (inner is FlateFilter)
                {
                    var inflated = ReadDeflate(input);
                    if (predictor <= 1) return inflated;
                }
                if (predictor > 1 &&
                    (inner is FlateFilter || inner is LzwFilter))
                {
                    int columns = Parameter(parameters, NameToken.Columns, 1);
                    int colors = Parameter(parameters, NameToken.Colors, 1);
                    int bits = Parameter(parameters, NameToken.BitsPerComponent, 8);
                    if (columns < 1 || columns > 131072 || colors < 1 || colors > 32 || bits < 1 || bits > 16)
                        throw new PdfDocumentFormatException("PDF predictor dimensions exceed the parsing limit.");
                }
                var decoded = inner.Decode(input, dictionary, provider, index);
                owner.Charge(decoded.Length);
                return decoded;
            }

            Memory<byte> ReadDeflate(Memory<byte> input)
            {
                if (input.Length < 2) throw new PdfDocumentFormatException("Invalid PDF zlib stream.");
                using (var source = new MemoryStream(input.ToArray(), false))
                using (var output = new MemoryStream())
                {
                    // PDF Flate data includes a zlib header. DeflateStream reads the raw DEFLATE stream.
                    source.Position = 2;
                    try
                    {
                        using (var deflate = new DeflateStream(source, CompressionMode.Decompress))
                        {
                            byte[] buffer = new byte[16 * 1024];
                            int length;
                            while ((length = deflate.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (output.Length + length > MaxDecodedStream)
                                    throw new PdfDocumentFormatException("PDF stream expansion budget exceeded.");
                                owner.Charge(length);
                                output.Write(buffer, 0, length);
                            }
                        }
                    }
                    catch (InvalidDataException) { throw new PdfDocumentFormatException("Invalid PDF DEFLATE stream."); }
                    return new Memory<byte>(output.GetBuffer(), 0, (int)output.Length);
                }
            }

            static int Parameter(DictionaryToken parameters, NameToken name, int fallback)
            {
                IToken value;
                if (!parameters.TryGet(name, out value)) return fallback;
                var number = value as NumericToken;
                return number == null ? fallback : number.Int;
            }
        }
    }
}
