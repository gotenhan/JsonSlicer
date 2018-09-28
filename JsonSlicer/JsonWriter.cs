using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JsonSlicer
{
    public interface IValueExtractorFactory<T>
    {
        Func<T, V> ValueExtractor<V>();
    }

    public struct PropertyValueExtractorFactory<T> : IValueExtractorFactory<T>
    {
        private readonly PropertyInfo _pi;

        public PropertyValueExtractorFactory(PropertyInfo pi)
        {
            _pi = pi;
        }

        public Func<T, V> ValueExtractor<V>()
        {
            var paramExpression = Expression.Parameter(typeof(T), "obj");
            var propertyExpressino = Expression.Property(paramExpression, _pi);
            var lambda = Expression.Lambda<Func<T, V>>(propertyExpressino, paramExpression);
            return lambda.Compile();
        }
    }

    public struct IdendityValueExtractor<T> : IValueExtractorFactory<T>
    {
        public Func<T, V> ValueExtractor<V>()
        {
            return t => (V)(object)t;
        }
    }

    public struct TypeSerializer
    {
        private static readonly ConcurrentDictionary<Type, TypeSerializer> Serializers =
            new ConcurrentDictionary<Type, TypeSerializer>();

        private JsonWriter.WriteDelegate<object> _serializer;

        private static readonly Type[] KnownTypes = new[]
        {
            typeof(string), typeof(decimal), typeof(double), typeof(float), typeof(int), typeof(long), typeof(short),
            typeof(byte), typeof(bool)
        };

        public TypeSerializer(JsonWriter.WriteDelegate<object> serializer)
        {
            _serializer = serializer;
        }

        public async ValueTask WriteAsync<T>(T t, PipeWriter writer)
        {
            try
            {
                await _serializer(t, writer).ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                writer.Complete(ex);
                throw;
            }
        }

        public static TypeSerializer Build<T>()
        {
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var actions = new List<JsonWriter.WriteDelegate<T>>(properties.Length);
            for (var i = 0; i < properties.Length; i++)
            {
                var prop = properties[i];
                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                var pt = prop.PropertyType;
                var valueExtractor = new PropertyValueExtractorFactory<T>(prop);
                var action = GetObjectWriter<T, PropertyValueExtractorFactory<T>>(pt, valueExtractor);

                var propName = new Property(prop.Name);

                actions.Add(async (t, writer) =>
                {
                    await JsonWriter.WriteAsync(propName, writer).ConfigureAwait(false);
                    await action(t, writer).ConfigureAwait(false);
                });
            }

            async ValueTask Serializer(T t, PipeWriter writer)
            {
                writer.Write(Token.BeginObject.Value);
                writer.Write(Token.CarriageReturn.Value);
                writer.Write(Token.LineFeed.Value);
                var count = actions.Count;
                for (var i = 0; i < count; i++)
                {
                    var a = actions[i];
                    await a(t, writer).ConfigureAwait(false);
                    if (i != count - 1)
                    {
                        writer.Write(Token.ValueSeparator.Value);
                        writer.Write(Token.CarriageReturn.Value);
                        writer.Write(Token.LineFeed.Value);
                    }
                }

                writer.Write(Token.CarriageReturn.Value);
                writer.Write(Token.LineFeed.Value);
                writer.Write(Token.EndObject.Value);
                await writer.FlushAsync().ConfigureAwait(false);
            }

            return new TypeSerializer((o, pw) => Serializer((T)o, pw));
        }

        private static JsonWriter.WriteDelegate<T> GetObjectWriter<T, VE>(Type pt, VE ve)
            where VE : IValueExtractorFactory<T>
        {
            var checkNull = !pt.IsValueType || Nullable.GetUnderlyingType(pt) != null;
            var propType = Nullable.GetUnderlyingType(pt) ?? pt;
            JsonWriter.WriteDelegate<T> action = null;

            if (KnownTypes.Contains(propType))
            {
                var makeActionMI =
                    typeof(TypeSerializer).GetMethod("MakeAction", BindingFlags.NonPublic | BindingFlags.Static);
                var makeAction = makeActionMI.MakeGenericMethod(typeof(T), propType, typeof(VE));
                action = (JsonWriter.WriteDelegate<T>)makeAction.Invoke(null, new object[] { ve });
            }
            else if (propType.IsArray)
            {
                var makeActionMI =
                    typeof(TypeSerializer).GetMethod("MakeArrayAction", BindingFlags.NonPublic | BindingFlags.Static);
                var makeAction = makeActionMI.MakeGenericMethod(typeof(T), propType, typeof(VE));
                action = (JsonWriter.WriteDelegate<T>)makeAction.Invoke(null, new object[] { ve });
            }
            else if (typeof(IEnumerable).IsAssignableFrom(propType))
            {
                var makeActionMI =
                    typeof(TypeSerializer).GetMethod("MakeEnumerableAction", BindingFlags.NonPublic | BindingFlags.Static);
                var makeAction = makeActionMI.MakeGenericMethod(typeof(T), propType, typeof(VE));
                action = (JsonWriter.WriteDelegate<T>)makeAction.Invoke(null, new object[] { ve });
            }
            else
            {
                var makeActionMI =
                    typeof(TypeSerializer).GetMethod("MakeAction", BindingFlags.NonPublic | BindingFlags.Static);
                var makeAction = makeActionMI.MakeGenericMethod(typeof(T), propType, typeof(VE));
                action = (JsonWriter.WriteDelegate<T>)makeAction.Invoke(null, new object[] { ve });
            }

            if (checkNull)
            {
                var action1 = action;
                var valFunc = ve.ValueExtractor<object>();
                action = (t, w) => valFunc(t) == null ? JsonWriter.WriteAsync(Token.Null, w) : action1(t, w);
            }

            return action;
        }

        private static JsonWriter.WriteDelegate<T> MakeAction<T, V, VE>(VE ve) where VE : IValueExtractorFactory<T>
        {
            var valFunc = ve.ValueExtractor<V>();
            var valWRiter = GetWriter<V>();
            return (tt, writer) =>
            {
                var v = valFunc(tt);
                return valWRiter(v, writer);
            };
        }

        private static JsonWriter.WriteDelegate<T> MakeArrayAction<T, V, VE>(VE ve) where VE : IValueExtractorFactory<T>
        {
            var valFunc = ve.ValueExtractor<V>();
            var valWRiter = GetArrayWriter<V>();
            return (tt, writer) =>
            {
                var v = valFunc(tt);
                return valWRiter(v, writer);
            };
        }

        private static JsonWriter.WriteDelegate<T> MakeEnumerableAction<T, V, VE>(VE ve) where VE : IValueExtractorFactory<T>
        {
            var valFunc = ve.ValueExtractor<V>();
            var valWRiter = GetEnumerableWriter<V>();
            return (tt, writer) =>
            {
                var v = valFunc(tt);
                return valWRiter(v, writer);
            };
        }

        private static JsonWriter.WriteDelegate<T> GetWriter<T>()
        {
            if (!KnownTypes.Contains(typeof(T)))
            {
                if (typeof(T) == typeof(object))
                {
                    return (t, writer) =>
                    {
                        var objWriter = GetObjectWriter<object, IdendityValueExtractor<object>>(t.GetType(), default);
                        return objWriter(t, writer);
                    };
                }
                else if (Serializers.TryGetValue(typeof(T), out var serializer))
                {
                    return serializer.WriteAsync;
                }
                else if (typeof(T).IsArray)
                {
                    return GetArrayWriter<T>();
                }
                else if (typeof(IEnumerable).IsAssignableFrom(typeof(T)))
                {
                    return GetEnumerableWriter<T>();
                }
            }

            var writerMethod = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "WriteAsync" &&
                                     !m.IsGenericMethod &&
                                     m.GetParameters().First().ParameterType == typeof(T));
            if (writerMethod is null)
            {
                writerMethod = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "WriteAsync" &&
                                     m.IsGenericMethod &&
                                     m.GetGenericArguments().Length == 1);

                writerMethod = writerMethod.MakeGenericMethod(typeof(T));
            }

            return (JsonWriter.WriteDelegate<T>)writerMethod.CreateDelegate(typeof(JsonWriter.WriteDelegate<T>));
        }

        private static JsonWriter.WriteDelegate<T> GetArrayWriter<T>()
        {
            var originalType = typeof(T);
            var elementType = originalType.GetElementType();
            var valueWriterGeneric = typeof(TypeSerializer).GetMethod("GetWriter", BindingFlags.NonPublic | BindingFlags.Static);
            var valueWriterGenerator = valueWriterGeneric.MakeGenericMethod(elementType);
            var valueWriter = valueWriterGenerator.Invoke(null, new object[]{});

            var arrayWriterGeneric = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "WriteAsync" &&
                                     m.IsGenericMethod &&
                                     m.GetGenericArguments().Length == 1 &&
                                     m.GetParameters().First().ParameterType.IsArray);
            var arrayWriter = arrayWriterGeneric.MakeGenericMethod(elementType);

            var valueWriterExpr = Expression.Constant(valueWriter, valueWriterGenerator.ReturnType);
            var arrParam = Expression.Parameter(typeof(T), "a");
            var writerParam = Expression.Parameter(typeof(PipeWriter), "writer");
            var body = Expression.Call(null,
                arrayWriter,
                arrParam,
                writerParam,
                valueWriterExpr);
            var expression = Expression.Lambda<JsonWriter.WriteDelegate<T>>(
                body,
                arrParam,
                writerParam);

            return expression.Compile();
        }

        private static JsonWriter.WriteDelegate<T> GetEnumerableWriter<T>()
        {
            var originalType = typeof(T);
            var valueWriterGeneric = typeof(TypeSerializer).GetMethod("GetWriter", BindingFlags.NonPublic | BindingFlags.Static);
            var elementType = originalType.IsGenericType ? originalType.GenericTypeArguments.First() : typeof(object);

            var enumerableType = originalType == typeof(IEnumerable) ? typeof(IEnumerable<object>) : originalType;
            var valueWriterGenerator = valueWriterGeneric.MakeGenericMethod(elementType);
            var valueWriter = valueWriterGenerator.Invoke(null, new object[]{});

            var enumerableWriterGeneric = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "WriteAsync" &&
                                     m.IsGenericMethod &&
                                     m.GetGenericArguments().Length == 2);
            var enumerableWriter = enumerableWriterGeneric.MakeGenericMethod(enumerableType, elementType);

            var eParam = Expression.Parameter(typeof(T), "e");
            var writerParam = Expression.Parameter(typeof(PipeWriter), "writer");

            var valueWriterExpr = Expression.Constant(valueWriter, valueWriterGenerator.ReturnType);
            var body = Expression.Call(
                null,
                enumerableWriter,
                eParam,
                writerParam,
                valueWriterExpr);
            var expression = Expression.Lambda<JsonWriter.WriteDelegate<T>>(body, eParam, writerParam);

            return expression.Compile();
        }

        public struct JsonWriter
        {
            public delegate ValueTask WriteDelegate<T>(T t, PipeWriter writer);

            private static readonly ThreadLocal<Encoding> UTF8 = new ThreadLocal<Encoding>(() => Encoding.UTF8);

            private static readonly ThreadLocal<Encoder> UTF8Enc =
                new ThreadLocal<Encoder>(() => Encoding.UTF8.GetEncoder());

            public static ValueTask WriteAsync<T>(T t, PipeWriter writer)
            {
                var serializer = Serializers.GetOrAdd(typeof(T), _ => Build<T>());
                return serializer.WriteAsync(t, writer);
            }

            public static ValueTask WriteAsync(object t, PipeWriter writer)
            {
                var objWriter = GetObjectWriter<object, IdendityValueExtractor<object>>(t.GetType(), default);
                return objWriter(t, writer);
            }

            public static async ValueTask WriteAsync<T>(T[] a, PipeWriter writer, WriteDelegate<T> writeValue)
            {
                writer.Write(Token.BeginArray.Value);

                for (var i = 0; i < a.Length; i++)
                {
                    await writeValue(a[i], writer).ConfigureAwait(false);
                    if (i != a.Length - 1)
                    {
                        writer.Write(Token.ValueSeparator.Value);
                    }
                }

                writer.Write(Token.EndArray.Value);
            }

            public static async ValueTask WriteAsync<E, T>(E e, PipeWriter writer, WriteDelegate<T> writeValue)
                where E : IEnumerable
            {
                writer.Write(Token.BeginArray.Value);
                var te = e.OfType<T>();
                var count = te.Count();
                foreach (var v in te)
                {
                    await writeValue(v, writer).ConfigureAwait(false);
                    if (--count > 0)
                    {
                        writer.Write(Token.ValueSeparator.Value);
                    }
                }

                writer.Write(Token.EndArray.Value);
            }

            public static ValueTask WriteAsync(Token t, PipeWriter writer)
            {
                writer.Write(t.Value);
                return default;
            }

            public static ValueTask WriteAsync(Property t, PipeWriter writer)
            {
                return WriteAsync(t.QuotedPropertyNameWithSeparator, writer);
            }

            public static ValueTask WriteAsync(string text, PipeWriter writer)
            {
                writer.Write(Token.StringDelimiter.Value);
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
                writer.Write(Token.StringDelimiter.Value);
                return default;
            }

            public static ValueTask WriteAsync(decimal dec, PipeWriter writer)
            {
                var mem = writer.GetSpan(64);
                _ = Utf8Formatter.TryFormat(dec, mem, out var bytesWritten)
                    ? true
                    : throw new ArgumentException(
                        $"Too long decimal {dec}");
                writer.Advance(bytesWritten);
                return default;
            }

            public static ValueTask WriteAsync(double dbl, PipeWriter writer)
            {
                var mem = writer.GetSpan(64);
                _ = Utf8Formatter.TryFormat(dbl, mem, out var bytesWritten)
                    ? true
                    : throw new ArgumentException(
                        $"Too long double {dbl}");
                writer.Advance(bytesWritten);
                return default;
            }

            public static ValueTask WriteAsync(float flt, PipeWriter writer)
            {
                var mem = writer.GetSpan(64);
                _ = Utf8Formatter.TryFormat(flt, mem, out var bytesWritten)
                    ? true
                    : throw new ArgumentException(
                        $"Too long float {flt}");
                writer.Advance(bytesWritten);
                return default;
            }

            public static ValueTask WriteAsync(int intg, PipeWriter writer)
            {
                var mem = writer.GetSpan(64);
                _ = Utf8Formatter.TryFormat(intg, mem, out var bytesWritten)
                    ? true
                    : throw new ArgumentException(
                        $"Too long int {intg}");
                writer.Advance(bytesWritten);
                return default;
            }

            public static ValueTask WriteAsync(long lng, PipeWriter writer)
            {
                var mem = writer.GetSpan(64);
                _ = Utf8Formatter.TryFormat(lng, mem, out var bytesWritten)
                    ? true
                    : throw new ArgumentException(
                        $"Too long long {lng}");
                writer.Advance(bytesWritten);
                return default;
            }

            public static ValueTask WriteAsync(short sht, PipeWriter writer)
            {
                var mem = writer.GetSpan(64);
                _ = Utf8Formatter.TryFormat(sht, mem, out var bytesWritten)
                    ? true
                    : throw new ArgumentException(
                        $"Too long short {sht}");
                writer.Advance(bytesWritten);
                return default;
            }

            public static ValueTask WriteAsync(byte bt, PipeWriter writer)
            {
                var mem = writer.GetSpan(64);
                _ = Utf8Formatter.TryFormat(bt, mem, out var bytesWritten)
                    ? true
                    : throw new ArgumentException(
                        $"Too long short {bt}");
                writer.Advance(bytesWritten);
                return default;
            }

            public static ValueTask WriteAsync(bool bl, PipeWriter writer)
            {
                writer.Write(bl ? Token.True.Value : Token.False.Value);
                return default;
            }
        }

        public struct Token
        {
            public byte[] Value;

            public Token(byte value)
            {
                Value = new[] { value };
            }

            public Token(byte[] value)
            {
                Value = value;
            }

            public Token(params Token[] values)
            {
                Value = values.SelectMany(v => v.Value).ToArray();
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
            public static readonly Token False = new Token(new byte[] { 0x66, 0x61, 0x6C, 0x73, 0x65 });
            public static readonly Token True = new Token(new byte[] { 0x74, 0x72, 0x75, 0x65 });
            public static readonly Token Null = new Token(new byte[] { 0x6E, 0x75, 0x6C, 0x6C });
        }

        public struct Property
        {
            public Token QuotedPropertyNameWithSeparator;

            public Property(string name)
            {
                var Value = Encoding.UTF8.GetBytes(name);
                QuotedPropertyNameWithSeparator = new Token(Token.StringDelimiter,
                    new Token(Value),
                    Token.StringDelimiter,
                    Token.NameSeparator,
                    Token.Space);
            }
        }
    }
}