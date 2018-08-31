using System;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace JsonSlicer
{
    public struct JsonWriter
    {
        [ThreadStatic]
        private static readonly Encoding UTF8 = Encoding.UTF8;

        [ThreadStatic]
        private static readonly Encoder UTF8Enc = Encoding.UTF8.GetEncoder();

        public async Task WriteAsync<T>(T t, PipeWriter writer)
        {
            await WriteAsync(Token.BeginObject, writer);
            await WriteAsync(Token.CarriageReturn, writer);
            await WriteAsync(Token.LineFeed, writer);

            var properties = t.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < properties.Length; i++)
            {
                var prop = properties[i];
                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                var propName = prop.Name;
                await WriteAsync(Token.StringDelimiter, writer);
                await WriteAsync(propName, writer);
                await WriteAsync(Token.StringDelimiter, writer);
                await WriteAsync(Token.NameSeparator, writer);
                await WriteAsync(Token.Space, writer);

                var propType = prop.PropertyType;
                var propValue = prop.GetValue(t);

                await WriteValue(writer, propValue, propType);

                if (i != properties.Length - 1)
                {
                    await WriteAsync(Token.ValueSeparator, writer);
                    await WriteAsync(Token.CarriageReturn, writer);
                    await WriteAsync(Token.LineFeed, writer);
                }
            }

            await WriteAsync(Token.CarriageReturn, writer);
            await WriteAsync(Token.LineFeed, writer);
            await WriteAsync(Token.EndObject, writer);
            await writer.FlushAsync();
        }

        private async Task WriteValue(PipeWriter writer, object propValue, Type propType)
        {
            switch (propValue)
            {
                case null:
                    await writer.WriteAsync(Token.Null.Value);
                    break;
                case string v:
                    await WriteAsync(Token.StringDelimiter, writer);
                    await WriteAsync(v, writer);
                    await WriteAsync(Token.StringDelimiter, writer);
                    break;
                case decimal v:
                    await WriteAsync(v, writer);
                    break;
                case double v:
                    await WriteAsync(v, writer);
                    break;
                case float v:
                    await WriteAsync(v, writer);
                    break;
                case int v:
                    await WriteAsync(v, writer);
                    break;
                case long v:
                    await WriteAsync(v, writer);
                    break;
                case short v:
                    await WriteAsync(v, writer);
                    break;
                case byte v:
                    await WriteAsync(v, writer);
                    break;
                case bool v:
                    await WriteAsync(v, writer);
                    break;
                case var e when propType.IsArray:
                    await CallArray(writer, propValue, propType.GetElementType());
                    break;
                case var e when typeof(IEnumerable).IsAssignableFrom(propType):
                    await CallEnumerable(writer, propValue, propType);
                    break;
                default:
                    await CallNested(writer, propValue, propType);
                    break;
            }
        }

        private async Task CallArray(PipeWriter writer, object propValue, Type elementType)
        {
            var method = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == nameof(WriteAsync) && 
                                     m.IsGenericMethod &&
                                     m.GetGenericArguments().Length == 1 &&
                                     m.GetParameters().First().ParameterType.IsArray);
            var methodInstance = method.MakeGenericMethod(elementType);
            await (Task) methodInstance.Invoke(this, new[] {propValue, writer });
        }

        private async Task CallEnumerable(PipeWriter writer, object propValue, Type originalType)
        {
            var method = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == nameof(WriteAsync) && m.IsGenericMethod && m.GetGenericArguments().Length == 2);
            var elementType = originalType.IsGenericType ? originalType.GenericTypeArguments.First() : typeof(object);
            var enumerableType = originalType.IsGenericType ? originalType : typeof(IEnumerable<object>);
            var value = originalType.IsGenericType ? propValue : ((IEnumerable) propValue).Cast<object>();
            var methodInstance = method.MakeGenericMethod(enumerableType, elementType);
            await (Task) methodInstance.Invoke(this, new[] {value, writer });
        }

        private async Task CallNested(PipeWriter writer, object propValue, Type propType)
        {
            var method = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == nameof(WriteAsync) && m.IsGenericMethod &&
                    m.GetGenericArguments().Length == 1 &&
                    !m.GetParameters().First().ParameterType.IsArray);
            var methodInstance = method.MakeGenericMethod(propType);
            await (Task) methodInstance.Invoke(this, new[] {propValue, writer });
        }

        public async Task WriteAsync<T>(T[] a, PipeWriter writer)
        {
            await writer.WriteAsync(Token.BeginArray.Value);
            for (int i = 0; i < a.Length; i++)
            {
                await WriteValue(writer, a[i], typeof(T));
                if (i != a.Length - 1)
                {
                    await WriteAsync(Token.ValueSeparator, writer);
                }
            }
            await writer.WriteAsync(Token.EndArray.Value);

        }

        public async Task WriteAsync<E, T>(E e, PipeWriter writer) where E : IEnumerable<T>
        {
            await writer.WriteAsync(Token.BeginArray.Value);
            var count = e.Count();
            foreach (var v in e)
            {
                await WriteValue(writer, v, v.GetType());
                if (--count > 0)
                {
                    await WriteAsync(Token.ValueSeparator, writer);
                }
            }
            await writer.WriteAsync(Token.EndArray.Value);
        }

        public async Task WriteAsync(Token t, PipeWriter writer)
        {
            await writer.WriteAsync(t.Value);
        }

        public async Task WriteAsync(string text, PipeWriter writer)
        {
            int totalCharsWritten = 0, charsWritten = 0;
            int totalBytesWritten = 0, bytesWritten = 0;
            bool completed = false;
            unsafe
            {
                fixed (char* textPtr = text)
                    do
                    {
                        var mem = writer.GetMemory(text.Length);
                        fixed (byte* bytes = &MemoryMarshal.GetReference(mem.Span))
                        {
                            UTF8Enc.Convert(textPtr + totalCharsWritten,
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

                UTF8Enc.Reset();
        }

        public async Task WriteAsync(decimal dec, PipeWriter writer)
        {
            var mem = writer.GetMemory(64);
            var _ = Utf8Formatter.TryFormat(dec, mem.Span, out int bytesWritten) ? true : throw new ArgumentException(
                $"Too long decimal {dec}");
            writer.Advance(bytesWritten);
        }

        public async Task WriteAsync(double dbl, PipeWriter writer)
        {
            var mem = writer.GetMemory(64);
            var _ = Utf8Formatter.TryFormat(dbl, mem.Span, out int bytesWritten) ? true : throw new ArgumentException(
                $"Too long double {dbl}");
            writer.Advance(bytesWritten);
        }

        public async Task WriteAsync(float flt, PipeWriter writer)
        {
            var mem = writer.GetMemory(64);
            var _ = Utf8Formatter.TryFormat(flt, mem.Span, out int bytesWritten) ? true : throw new ArgumentException(
                $"Too long float {flt}");
            writer.Advance(bytesWritten);
        }

        public async Task WriteAsync(int intg, PipeWriter writer)
        {
            var mem = writer.GetMemory(64);
            var _ = Utf8Formatter.TryFormat(intg, mem.Span, out int bytesWritten) ? true : throw new ArgumentException(
                $"Too long int {intg}");
            writer.Advance(bytesWritten);
        }

        public async Task WriteAsync(long lng, PipeWriter writer)
        {
            var mem = writer.GetMemory(64);
            var _ = Utf8Formatter.TryFormat(lng, mem.Span, out int bytesWritten) ? true : throw new ArgumentException(
                $"Too long long {lng}");
            writer.Advance(bytesWritten);
        }

        public async Task WriteAsync(short sht, PipeWriter writer)
        {
            var mem = writer.GetMemory(64);
            var _ = Utf8Formatter.TryFormat(sht, mem.Span, out int bytesWritten) ? true : throw new ArgumentException(
                $"Too long short {sht}");
            writer.Advance(bytesWritten);
        }

        public async Task WriteAsync(byte bt, PipeWriter writer)
        {
            var mem = writer.GetMemory(64);
            var _ = Utf8Formatter.TryFormat(bt, mem.Span, out int bytesWritten) ? true : throw new ArgumentException(
                $"Too long short {bt}");
            writer.Advance(bytesWritten);
        }

        public async Task WriteAsync(bool bl, PipeWriter writer)
        {
            await writer.WriteAsync(bl ? Token.True.Value : Token.False.Value);
        }

        public struct Token
        {
            public byte[] Value;

            public Token(byte value)
            {
                Value = new byte[]{value};
            }

            public Token(byte[] value)
            {
                Value = value;
            }

            public static readonly Token BeginObject = new Token(0x7B);
            public static readonly Token EndObject = new Token(0x7D);
            public static readonly Token BeginArray = new Token(0x05B);
            public static readonly Token EndArray = new Token(0x05D);
            public static readonly Token ValueSeparator = new Token(0x2C);
            public static readonly Token NameSeparator = new Token(0x3A);
            public static readonly Token Space = new Token(0x20);
            public static readonly Token HorizontalTab = new Token(0x09);
            public static readonly Token LineFeed = new Token(0x0A);
            public static readonly Token CarriageReturn = new Token(0x0D);

            public static readonly Token StringDelimiter = new Token(0x22);
            public static readonly Token False = new Token(new byte[] { 0x66, 0x61, 0x6C, 0x73, 0x65});
            public static readonly Token True = new Token(new byte[] { 0x74, 0x72, 0x75, 0x65});
            public static readonly Token Null = new Token(new byte[] { 0x6E, 0x75, 0x6C, 0x6C});
        }
    }
}