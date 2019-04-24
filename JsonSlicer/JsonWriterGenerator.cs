using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JsonSlicer
{
    public class JsonWriterGenerator
    {
        private static readonly ConcurrentDictionary<Type, ValueTuple<IJsonWriter, byte[]>> Generators = new ConcurrentDictionary<Type, ValueTuple<IJsonWriter, byte[]>>();
        
        private static readonly MethodInfo GenericGenerate = typeof(JsonWriterGenerator)
            .GetMethod(nameof(Generate), new Type[] { });

        public static IJsonWriter Generate(Type t)
        {
            var mi = GenericGenerate.MakeGenericMethod(t);
            return (IJsonWriter) mi.Invoke(null, new object[] { });
        }

        public static IJsonWriter<T> Generate<T>()
        {
            var (writer, assemblyBytes) = Generators.GetOrAdd(typeof(T), _ => GenerateImpl<T>());
            return writer as IJsonWriter<T>;
        }

        private static (IJsonWriter<T> writer, byte[] assemblyBytes) GenerateImpl<T>()
        {
            var serializerTemplate = new SerializerTemplate(typeof(T));
            var serializer = serializerTemplate.Generate();
            var cSharpParseOptions =
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular);
#if DEBUG
            var serializerName = serializerTemplate.SerializerName;
            var csPath = Path.GetFullPath(serializerName + ".cs");
            File.WriteAllText(csPath, serializer.Text, Encoding.UTF8);
            var tree = SyntaxFactory.ParseSyntaxTree(serializer.Text, cSharpParseOptions, csPath, Encoding.UTF8);
#else
            var tree = SyntaxFactory.ParseCompilationUnit(serializer.Text, 0, cSharpParseOptions).SyntaxTree;
#endif

            var systemAssembliesLocations = GetNetCoreSystemAssemblies();
            var commonReferences = systemAssembliesLocations.Concat(new[]
                {
                    typeof(Pipe).GetTypeInfo().Assembly.Location,
                    typeof(BuffersExtensions).GetTypeInfo().Assembly.Location,
                    typeof(IJsonWriter<>).GetTypeInfo().Assembly.Location,
                    typeof(T).GetTypeInfo().Assembly.Location,
                })
                .Select(al => MetadataReference.CreateFromFile(al));
            var typeSpecificReferences = 
                serializer.ReferencedTypes.Select(GetMetadataReferenceForType);

            var cSharpCompilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                platform: Platform.AnyCpu);
            var compilation = CSharpCompilation.Create(
                serializerTemplate.SerializerName,
                new[] {tree},
                commonReferences.Concat(typeSpecificReferences),
                cSharpCompilationOptions);

            Assembly assembly = null;
            byte[] assemblyBytes = null;
#if DEBUG
            var dllPath = Path.GetFullPath(serializerName + ".dll");
            var pdbPath = Path.GetFullPath(serializerName + ".pdb");
            File.Delete(dllPath);
            File.Delete(pdbPath);
            var er = compilation.Emit(dllPath, pdbPath);
#else
            using (var ms = new MemoryStream())
            {

                var er = compilation.Emit(ms);
#endif
                if (!er.Success)
                {
                    var sourceWithNumberedLines =
                        tree.GetText().Lines.Select(l => $"{l.LineNumber}:    {l.Text.ToString(l.Span)}");
                    throw new ApplicationException(
                        string.Join(Environment.NewLine,
                            er.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                                .Cast<object>()
                                .Concat(new[] {"========="})
                                .Concat(sourceWithNumberedLines)));
                }
#if !DEBUG
                else
                { 
                    ms.Seek(0, SeekOrigin.Begin);
                    assemblyBytes = ms.ToArray();
                    assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(ms);
                }
            }
#else
            assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
            assemblyBytes = File.ReadAllBytes(dllPath);
#endif

            var writer = (IJsonWriter<T>) Activator.CreateInstance(assembly.DefinedTypes.First());
            return (writer, assemblyBytes);

            PortableExecutableReference GetMetadataReferenceForType(Type t)
            {
                (IJsonWriter _, byte[] assembly) generator;
                if (Generators.TryGetValue(t, out generator))
                {
                    return MetadataReference.CreateFromImage(generator.assembly);
                }
                else if (!string.IsNullOrEmpty(t.GetTypeInfo().Assembly.Location))
                {
                    return MetadataReference.CreateFromFile(t.GetTypeInfo().Assembly.Location);
                }
                else
                {
                    throw new ApplicationException(
                        $"Serializer for {typeof(T).FullName} requires serializer for {t.FullName}, but it was not generated");
                }
            }
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
             "netstandard",
             "System.Collections",
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