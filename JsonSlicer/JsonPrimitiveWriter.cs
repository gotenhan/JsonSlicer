using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JsonSlicer
{
    public sealed class JsonPrimitiveWriter:
        IJsonWriter,
        IJsonWriter<string>,
        IJsonWriter<byte>,
        IJsonWriter<short>,
        IJsonWriter<int>,
        IJsonWriter<long>,
        IJsonWriter<float>,
        IJsonWriter<double>,
        IJsonWriter<decimal>,
        IJsonWriter<bool>
    {
        public static readonly JsonPrimitiveWriter Instance = new JsonPrimitiveWriter();
        
        private static readonly ThreadLocal<Encoder> UTF8Enc =
            new ThreadLocal<Encoder>(() => Encoding.UTF8.GetEncoder());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(object _, PipeWriter writer)
        {
            Write(Token.EmptyObject, writer);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(Token t, PipeWriter writer)
        {
            writer.Write(t.Value);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(Property t, PipeWriter writer)
        {
            Write(t.QuotedPropertyNameWithSeparator, writer);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(string text, PipeWriter writer)
        {
            Write(Token.StringDelimiter, writer);
            //int totalCharsWritten = 0, charsWritten = 0;
            //int totalBytesWritten = 0, bytesWritten = 0;
            //var completed = false;
            //unsafe
            //{
            //    fixed (char* textPtr = text)
            //    {
            //        do
            //        {
            //            var mem = writer.GetSpan(text.Length);
            //            fixed (byte* bytes = &MemoryMarshal.GetReference(mem))
            //            {
            //                UTF8Enc.Value.Convert(textPtr + totalCharsWritten,
            //                    text.Length - totalCharsWritten,
            //                    bytes,
            //                    mem.Length,
            //                    false,
            //                    out charsWritten,
            //                    out bytesWritten,
            //                    out completed);
            //                totalCharsWritten += charsWritten;
            //                totalBytesWritten += bytesWritten;
            //            }

            //            writer.Advance(bytesWritten);
            //        } while (!completed);
            //    }

            //}

            //UTF8Enc.Value.Reset();
            int bytesWritten, expectedByteCount;
            byte retries = 2;
            do
            {
                expectedByteCount = UTF8Enc.Value.GetByteCount(text.AsSpan(), true);
                var mem = writer.GetSpan(expectedByteCount);
                bytesWritten = UTF8Enc.Value.GetBytes(text.AsSpan(), mem, true);
            } while (expectedByteCount != bytesWritten && retries-- > 0);

            if (retries <= 0)
            {
                throw new ApplicationException($"Could not write string {text} within 2 retries");
            }
            writer.Advance(bytesWritten);

            Write(Token.StringDelimiter, writer);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(decimal dec, PipeWriter writer)
        {
            var mem = writer.GetSpan(64);
            _ = Utf8Formatter.TryFormat(dec, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long decimal {dec}");
            writer.Advance(bytesWritten);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(double dbl, PipeWriter writer)
        {
            var mem = writer.GetSpan(64);
            _ = Utf8Formatter.TryFormat(dbl, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long double {dbl}");
            writer.Advance(bytesWritten);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(float flt, PipeWriter writer)
        {
            var mem = writer.GetSpan(64);
            _ = Utf8Formatter.TryFormat(flt, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long float {flt}");
            writer.Advance(bytesWritten);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(int intg, PipeWriter writer)
        {
            var mem = writer.GetSpan(32);
            _ = Utf8Formatter.TryFormat(intg, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long int {intg}");
            writer.Advance(bytesWritten);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(long lng, PipeWriter writer)
        {
            var mem = writer.GetSpan(64);
            _ = Utf8Formatter.TryFormat(lng, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long long {lng}");
            writer.Advance(bytesWritten);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(short sht, PipeWriter writer)
        {
            var mem = writer.GetSpan(8);
            _ = Utf8Formatter.TryFormat(sht, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long short {sht}");
            writer.Advance(bytesWritten);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(byte bt, PipeWriter writer)
        {
            var mem = writer.GetSpan(4);
            _ = Utf8Formatter.TryFormat(bt, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long short {bt}");
            writer.Advance(bytesWritten);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask Write(bool bl, PipeWriter writer)
        {
            writer.Write(bl ? Token.True.Value : Token.False.Value);
            return default;
        }
    }
}