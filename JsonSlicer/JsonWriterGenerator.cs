using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JsonSlicer
{
    public class JsonWriterGenerator
    {
        private static ConcurrentDictionary<Type, IJsonWriter> generators = new ConcurrentDictionary<Type, IJsonWriter>();
        
        public static MethodInfo GenericGenerate = typeof(JsonWriterGenerator)
            .GetMethod(nameof(Generate), new Type[] { });

        public IJsonWriter Generate(Type t)
        {
            var mi = GenericGenerate.MakeGenericMethod(t);
            return (IJsonWriter) mi.Invoke(this, new object[] { });
        }

        public IJsonWriter<T> Generate<T>()
        {
            return generators.GetOrAdd(typeof(T), (_) => GenerateImpl<T>()) as IJsonWriter<T>;
        }

        private static IJsonWriter<T> GenerateImpl<T>()
        {
            var serializedType = typeof(T).FullName;
            var template = new SerializerTemplate(typeof(T));
            var c = template.Template();
            var cSharpParseOptions =
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);
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
                    var sourceWithNumberedLines =
                        tree.GetText().Lines.Select(l => $"{l.LineNumber}:    {l.Text.ToString(l.Span)}");
                    throw new ApplicationException(
                        string.Join(Environment.NewLine,
                            er.Diagnostics.Cast<object>()
                                .Concat(new[] {"========="})
                                .Concat(sourceWithNumberedLines)));
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
             "netstandard",
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