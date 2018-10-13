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
            var serializerTemplate = new SerializerTemplate(typeof(T));
            var c = serializerTemplate.Generate();
            var cSharpParseOptions =
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);
#if DEBUG
            var serializerName = serializerTemplate.SerializerName;
            var csPath = Path.GetFullPath(serializerName + ".cs");
            File.WriteAllBytes(csPath, Encoding.UTF8.GetBytes(c));
            var tree = SyntaxFactory.ParseSyntaxTree(c, cSharpParseOptions, csPath, Encoding.UTF8);
#else
            var tree = SyntaxFactory.ParseCompilationUnit(c, 0, cSharpParseOptions).SyntaxTree;
#endif

            var systemAssembliesLocations = GetNetCoreSystemAssemblies();
            var references = systemAssembliesLocations.Concat(new[]
                {
                    typeof(Pipe).GetTypeInfo().Assembly.Location,
                    typeof(BuffersExtensions).GetTypeInfo().Assembly.Location,
                    typeof(IJsonWriter<>).GetTypeInfo().Assembly.Location,
                    typeof(T).GetTypeInfo().Assembly.Location,
                })
                .Select(al => MetadataReference.CreateFromFile(al));

            var cSharpCompilationOptions = new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                platform: Platform.AnyCpu);
            var compilation = CSharpCompilation.Create(
                serializerTemplate.SerializerName,
                new[] {tree},
                references,
                cSharpCompilationOptions);

            Assembly assembly = null;
#if DEBUG
            var dllPath = Path.GetFullPath(serializerName + ".dll");
            var pdbPath = Path.GetFullPath(serializerName + ".pdb");
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
                            er.Diagnostics.Cast<object>()
                                .Concat(new[] {"========="})
                                .Concat(sourceWithNumberedLines)));
                }
#if !DEBUG
                else
                { 
                    ms.Seek(0, SeekOrigin.Begin);
                    assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(ms);
                }
            }
#else
            assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
#endif

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