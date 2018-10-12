using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JsonSlicer
{
    public static class JsonWriter
    {
        private static readonly ConcurrentDictionary<Type, IJsonWriter> Serializers =
            new ConcurrentDictionary<Type, IJsonWriter>();

        private static readonly ThreadLocal<Encoder> UTF8Enc =
            new ThreadLocal<Encoder>(() => Encoding.UTF8.GetEncoder());

        public static ValueTask WriteObject<T>(T t, PipeWriter writer)
        {
            var serializer = Serializers.GetOrAdd(typeof(T), _ => new JsonWriterGenerator().Generate<T>());
            return serializer.Write(t, writer);
        }

        public static async ValueTask WriteArray<T, TValWriter>(T[] a, PipeWriter writer, TValWriter valueWriter)
            where TValWriter: IJsonWriter<T>
        {
            WritePrimitive(Token.BeginArray, writer);

            for (var i = 0; i < a.Length; i++)
            {
                await valueWriter.Write(a[i], writer).ConfigureAwait(false);
                if (i != a.Length - 1)
                {
                    WritePrimitive(Token.ValueSeparator, writer);
                }
            }

            WritePrimitive(Token.EndArray, writer);
        }

        public static async ValueTask WriteEnumerable<E, T, TValWriter>(E e, PipeWriter writer, TValWriter valueWriter)
            where E : IEnumerable
            where TValWriter : IJsonWriter<T>
        {
            writer.Write(Token.BeginArray.Value);
            var te = e.OfType<T>();
            var count = te.Count();
            foreach (var v in te)
            {
                await valueWriter.Write(v, writer).ConfigureAwait(false);
                if (--count > 0)
                {
                    writer.Write(Token.ValueSeparator.Value);
                }
            }

            writer.Write(Token.EndArray.Value);
        }

        public static void WritePrimitive(Token t, PipeWriter writer)
        {
            writer.Write(t.Value);
        }

        public static void WritePrimitive(Property t, PipeWriter writer)
        {
            WritePrimitive(t.QuotedPropertyNameWithSeparator, writer);
        }

        public static void WritePrimitive(string text, PipeWriter writer)
        {
            WritePrimitive(Token.StringDelimiter, writer);
            int totalCharsWritten = 0, charsWritten = 0;
            int totalBytesWritten = 0, bytesWritten = 0;
            var completed = false;
            unsafe
            {
                fixed (char* textPtr = text)
                {
                    do
                    {
                        var mem = writer.GetSpan(text.Length);
                        fixed (byte* bytes = &MemoryMarshal.GetReference(mem))
                        {
                            UTF8Enc.Value.Convert(textPtr + totalCharsWritten,
                                text.Length - totalCharsWritten,
                                bytes,
                                mem.Length,
                                false,
                                out charsWritten,
                                out bytesWritten,
                                out completed);
                            totalCharsWritten += charsWritten;
                            totalBytesWritten += bytesWritten;
                        }

                        writer.Advance(bytesWritten);
                    } while (!completed);
                }
            }

            UTF8Enc.Value.Reset();
            WritePrimitive(Token.StringDelimiter, writer);
        }

        public static void WritePrimitive(decimal dec, PipeWriter writer)
        {
            var mem = writer.GetSpan(64);
            _ = Utf8Formatter.TryFormat(dec, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long decimal {dec}");
            writer.Advance(bytesWritten);
        }

        public static void WritePrimitive(double dbl, PipeWriter writer)
        {
            var mem = writer.GetSpan();
            _ = Utf8Formatter.TryFormat(dbl, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long double {dbl}");
            writer.Advance(bytesWritten);
        }

        public static void WritePrimitive(float flt, PipeWriter writer)
        {
            var mem = writer.GetSpan(64);
            _ = Utf8Formatter.TryFormat(flt, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long float {flt}");
            writer.Advance(bytesWritten);
        }

        public static void WritePrimitive(int intg, PipeWriter writer)
        {
            var mem = writer.GetSpan(32);
            _ = Utf8Formatter.TryFormat(intg, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long int {intg}");
            writer.Advance(bytesWritten);
        }

        public static void WritePrimitive(long lng, PipeWriter writer)
        {
            var mem = writer.GetSpan(64);
            _ = Utf8Formatter.TryFormat(lng, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long long {lng}");
            writer.Advance(bytesWritten);
        }

        public static void WritePrimitive(short sht, PipeWriter writer)
        {
            var mem = writer.GetSpan(8);
            _ = Utf8Formatter.TryFormat(sht, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long short {sht}");
            writer.Advance(bytesWritten);
        }

        public static void WritePrimitive(byte bt, PipeWriter writer)
        {
            var mem = writer.GetSpan(4);
            _ = Utf8Formatter.TryFormat(bt, mem, out var bytesWritten)
                ? true
                : throw new ArgumentException(
                    $"Too long short {bt}");
            writer.Advance(bytesWritten);
        }

        public static void WritePrimitive(bool bl, PipeWriter writer)
        {
            writer.Write(bl ? Token.True.Value : Token.False.Value);
        }
    }
}