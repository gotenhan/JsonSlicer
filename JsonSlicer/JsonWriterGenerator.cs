using System;
using System.Buffers;
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
            var template = new SerializerTemplate(typeof(T));
            var c = template.TransformText();
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