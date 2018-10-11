using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
            return t => (V) (object) t;
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
            catch (Exception ex)
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
                    JsonWriter.WritePrimitive(propName, writer);
                    await action(t, writer).ConfigureAwait(false);
                });
            }

            async ValueTask Serializer(T t, PipeWriter writer)
            {
                writer.Write(Token.BeginObject.Value);
                writer.Write(Token.NewLine.Value);
                var count = actions.Count;
                for (var i = 0; i < count; i++)
                {
                    var a = actions[i];
                    await a(t, writer).ConfigureAwait(false);
                    if (i != count - 1)
                    {
                        writer.Write(Token.ValueSeparator.Value);
                        writer.Write(Token.NewLine.Value);
                    }
                }

                writer.Write(Token.NewLine.Value);
                writer.Write(Token.EndObject.Value);
                await writer.FlushAsync().ConfigureAwait(false);
            }

            return new TypeSerializer((o, pw) => Serializer((T) o, pw));
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
                action = (JsonWriter.WriteDelegate<T>) makeAction.Invoke(null, new object[] {ve});
            }
            else if (propType.IsArray)
            {
                var makeActionMI =
                    typeof(TypeSerializer).GetMethod("MakeArrayAction", BindingFlags.NonPublic | BindingFlags.Static);
                var makeAction = makeActionMI.MakeGenericMethod(typeof(T), propType, typeof(VE));
                action = (JsonWriter.WriteDelegate<T>) makeAction.Invoke(null, new object[] {ve});
            }
            else if (typeof(IEnumerable).IsAssignableFrom(propType))
            {
                var makeActionMI =
                    typeof(TypeSerializer).GetMethod("MakeEnumerableAction",
                        BindingFlags.NonPublic | BindingFlags.Static);
                var makeAction = makeActionMI.MakeGenericMethod(typeof(T), propType, typeof(VE));
                action = (JsonWriter.WriteDelegate<T>) makeAction.Invoke(null, new object[] {ve});
            }
            else
            {
                var makeActionMI =
                    typeof(TypeSerializer).GetMethod("MakeAction", BindingFlags.NonPublic | BindingFlags.Static);
                var makeAction = makeActionMI.MakeGenericMethod(typeof(T), propType, typeof(VE));
                action = (JsonWriter.WriteDelegate<T>) makeAction.Invoke(null, new object[] {ve});
            }

            if (checkNull)
            {
                var action1 = action;
                var valFunc = ve.ValueExtractor<object>();
                action = (t, w) =>
                {
                    if (valFunc(t) == null)
                    {
                        JsonWriter.WritePrimitive(Token.Null, w);
                        return default;
                    }
                    else
                    {
                        return action1(t, w);
                    }
                };
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

        private static JsonWriter.WriteDelegate<T> MakeEnumerableAction<T, V, VE>(VE ve)
            where VE : IValueExtractorFactory<T>
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
                .FirstOrDefault(m => m.Name == "WritePrimitive" &&
                                     !m.IsGenericMethod &&
                                     m.GetParameters().First().ParameterType == typeof(T));
            if (writerMethod is null)
            {
                writerMethod = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "WriteObject" &&
                                         m.IsGenericMethod &&
                                         m.GetGenericArguments().Length == 1);

                writerMethod = writerMethod.MakeGenericMethod(typeof(T));
                return (JsonWriter.WriteDelegate<T>) writerMethod.CreateDelegate(typeof(JsonWriter.WriteDelegate<T>));
            }
            else
            {
                var @delegate = (Action<T, PipeWriter>) writerMethod.CreateDelegate(typeof(Action<T, PipeWriter>));
                return (t, pw) =>
                {
                    @delegate.Invoke(t, pw);
                    return default;
                };
            }
        }

        private static JsonWriter.WriteDelegate<T> GetArrayWriter<T>()
        {
            var originalType = typeof(T);
            var elementType = originalType.GetElementType();
            var valueWriterGeneric =
                typeof(TypeSerializer).GetMethod("GetWriter", BindingFlags.NonPublic | BindingFlags.Static);
            var valueWriterGenerator = valueWriterGeneric.MakeGenericMethod(elementType);
            var valueWriter = valueWriterGenerator.Invoke(null, new object[] { });

            var arrayWriterGeneric = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "WriteArray" &&
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
            var valueWriterGeneric =
                typeof(TypeSerializer).GetMethod("GetWriter", BindingFlags.NonPublic | BindingFlags.Static);
            var elementType = originalType.IsGenericType ? originalType.GenericTypeArguments.First() : typeof(object);

            var enumerableType = originalType == typeof(IEnumerable) ? typeof(IEnumerable<object>) : originalType;
            var valueWriterGenerator = valueWriterGeneric.MakeGenericMethod(elementType);
            var valueWriter = valueWriterGenerator.Invoke(null, new object[] { });

            var enumerableWriterGeneric = typeof(JsonWriter).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "WriteEnumerable" &&
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

            private static readonly ThreadLocal<Encoder> UTF8Enc =
                new ThreadLocal<Encoder>(() => Encoding.UTF8.GetEncoder());

            public static ValueTask WriteObject<T>(T t, PipeWriter writer)
            {
                var serializer = Serializers.GetOrAdd(typeof(T), _ => Build<T>());
                return serializer.WriteAsync(t, writer);
            }

            public static ValueTask WriteObject(object t, PipeWriter writer)
            {
                var objWriter = GetObjectWriter<object, IdendityValueExtractor<object>>(t.GetType(), default);
                return objWriter(t, writer);
            }

            public static async ValueTask WriteArray<T>(T[] a, PipeWriter writer, WriteDelegate<T> writeValue)
            {
                WritePrimitive(Token.BeginArray, writer);

                for (var i = 0; i < a.Length; i++)
                {
                    await writeValue(a[i], writer).ConfigureAwait(false);
                    if (i != a.Length - 1)
                    {
                        WritePrimitive(Token.ValueSeparator, writer);
                    }
                }

                WritePrimitive(Token.EndArray, writer);
            }

            public static async ValueTask WriteEnumerable<E, T>(E e, PipeWriter writer, WriteDelegate<T> writeValue)
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

        public struct Token
        {
            public byte[] Value;

            public Token(byte value)
            {
                Value = new[] {value};
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
            public static readonly Token NewLine = new Token(Encoding.UTF8.GetBytes(Environment.NewLine));

            public static readonly Token StringDelimiter = new Token(0x22);
            public static readonly Token False = new Token(new byte[] {0x66, 0x61, 0x6C, 0x73, 0x65});
            public static readonly Token True = new Token(new byte[] {0x74, 0x72, 0x75, 0x65});
            public static readonly Token Null = new Token(new byte[] {0x6E, 0x75, 0x6C, 0x6C});
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

    public interface IJsonWriter
    {
        ValueTask Write(object o, PipeWriter writer);
    }

    public interface IJsonWriter<T> : IJsonWriter
    {
        ValueTask Write(T t, PipeWriter writer);
    }

    public class JsonWriterGenerator
    {
        public static MethodInfo GenericGenerate =
            typeof(JsonWriterGenerator).GetMethod(nameof(Generate), BindingFlags.Instance | BindingFlags.Public);

        public IJsonWriter Generate(Type t)
        {
            var mi = GenericGenerate.MakeGenericMethod(t);
            return (IJsonWriter) mi.Invoke(this, new object[] { });
        }

        public IJsonWriter<T> Generate<T>()
        {
            var serializedType = typeof(T).FullName;
            var serializerType = $"JsonWriter_{typeof(T).Name}_{Math.Abs(serializedType.GetHashCode())}";
            var c = $@"
namespace JsonSlicer.GeneratedSerializers 
{{
  using global::System.Buffers;
  public class {serializerType} : global::JsonSlicer.IJsonWriter<global::{serializedType}>
  {{
    public global::System.Threading.Tasks.ValueTask Write(object obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {{
        pipeWriter.Write(System.Text.Encoding.UTF8.GetBytes(""Writing object of type {typeof(T).Name}""));
        return default; 
    }}

    public global::System.Threading.Tasks.ValueTask Write(global::{serializedType} obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {{
        pipeWriter.Write(System.Text.Encoding.UTF8.GetBytes(""Writing object of type {typeof(T).Name}""));
        return default; 
    }}
  }}
}}";
            var cSharpParseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);
            var tree = SyntaxFactory.ParseCompilationUnit(c, 0, cSharpParseOptions).SyntaxTree;

            var systemAssembliesLocations = GetNetCoreSystemAssemblies();
            var references = systemAssembliesLocations.Concat(new[]
            {
                typeof(Pipe).GetTypeInfo().Assembly.Location,
                typeof(BuffersExtensions).GetTypeInfo().Assembly.Location,
                typeof(IJsonWriter<>).GetTypeInfo().Assembly.Location,
                typeof(T).GetTypeInfo().Assembly.Location,
            })
            .Select(al => MetadataReference.CreateFromFile(al));

            var compilation = CSharpCompilation.Create(
                serializedType,
                new[] {tree},
                references,
                new CSharpCompilationOptions(
                    outputKind: OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Debug,
                    platform: Platform.AnyCpu));

            Assembly assembly = null;
            using (var ms = new MemoryStream())
            {
                var er = compilation.Emit(ms);
                if (er.Success)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(ms);
                }
                else
                {
                    throw new ApplicationException(
                        string.Join(Environment.NewLine, er.Diagnostics.Select(d => d.ToString())) +
                        Environment.NewLine + "===============" + Environment.NewLine +
                        tree.GetText());
                }
            }

            return (IJsonWriter<T>) Activator.CreateInstance(assembly.DefinedTypes.First());
        }

        private static IEnumerable<string> GetNetCoreSystemAssemblies()
        {
            var trustedAssembliesPaths =
                ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
            var systemAssemblies = new[]
            {
//                "mscorlib",
//                "System",
//                "System.Core",
                "System.Runtime",
                "System.Private.CoreLib",
//                "System.Threading.Tasks",
                "System.Threading.Tasks.Extensions"
            };
            var systemAssembliesLocations = systemAssemblies
                .Select(a => trustedAssembliesPaths.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == a))
                .Where(x => x != null);
            return systemAssembliesLocations;
        }
    }
}