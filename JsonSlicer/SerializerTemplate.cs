using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace JsonSlicer
{
    public  class SerializerTemplate
    {
        public string SerializerName { get; }
        public Type SerializedType { get; }
        public string SerializedTypePath { get; }

        private static readonly Type[] KnownTypes = new[]
        {
            typeof(string), typeof(decimal), typeof(double), typeof(float), typeof(int), typeof(long), typeof(short),
            typeof(byte), typeof(bool), typeof(object)
        };

        public SerializerTemplate(Type serializedType)
        {
            SerializedType = serializedType;
            unchecked
            {
                var hashCode = serializedType.FullName.Aggregate(1, (h, c) => h * 397 + c);
                var normalizedName = GetNormalizedTypeName(serializedType);
                SerializerName = $"JsonWriter_{normalizedName}_{2L*Math.Abs(hashCode)}";
            }
            SerializedTypePath = "global::" + GetNestedTypePath(serializedType);
        }

        private static string GetNormalizedTypeName(Type serializedType)
        {
            return serializedType.Name.Replace("[]", "__ARRAY__").Replace("`1", "__1ParamGeneric__");
        }

        private string GetNestedTypePath(Type type)
        {
            if (type.IsNested)
            {
                return GetNestedTypePath(type.DeclaringType) + "." + TransformGenericParameters(type, includeNamespace: false);
            }
            else
            {
                return TransformGenericParameters(type, includeNamespace: true);
            }
        }

        private string TransformGenericParameters(Type type, bool includeNamespace)
        {
            var name = type.Name;
            if (type.IsGenericType)
            {
                var genericArgs = type.GetGenericArguments().Select(GetNestedTypePath);
                var genericType = "<" + string.Join(",", genericArgs) + ">";
                name = name.Substring(0, name.IndexOf('`')) + genericType;
            }

            return includeNamespace ? type.Namespace + "." + name : name;
        }

        private T CastObject<T>(object obj)
        {
            return (T)obj;	
        }

        public string Generate() => $@"
namespace JsonSlicer.GeneratedSerializers 
{{
  using global::System.Buffers;

  public class {SerializerName} : global::JsonSlicer.IJsonWriter<{ SerializedTypePath }>
  {{
    public global::System.Threading.Tasks.ValueTask Write(object obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {{
        if(obj is null)
            {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter); return default; }}
		if(typeof({SerializedTypePath}).IsAssignableFrom(obj.GetType()))
			return Write(({SerializedTypePath})obj, pipeWriter);
		else
			throw new System.ArgumentException($""Expected obj to be of type { SerializedType.FullName } but received {{obj?.GetType()}}"");
    }}

    public async global::System.Threading.Tasks.ValueTask Write({SerializedTypePath} obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {{
		{ WriteTypeTemplate(SerializedType, "obj") }
    }}
  }}
}}
";

        private string WriteTypeTemplate(Type type, string value)
        {
            if (KnownTypes.Contains(type))
            {
                return $@"global::JsonSlicer.JsonPrimitiveWriter.Instance.Write({value}, pipeWriter);";
            }
            else if (type.IsArray)
            {
                var valueType = type.GetElementType();
                return WriteArrayTemplate(valueType, value);
            }
            else if (typeof(IEnumerable<>).IsAssignableFrom(type) || typeof(IEnumerable).IsAssignableFrom(type))
            {
                var valueType = type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
                return WriteEnumerableTemplate(valueType, value);
            }

            return "return default;";
        }

        private string WriteArrayTemplate(Type valueType, string arrayName)
        {
            var endLabel = "Label" + Guid.NewGuid().ToString("N");
            var arrayCountVar = "arrayCount" + Guid.NewGuid().ToString("N");
            var indexVar = "i" + Guid.NewGuid().ToString("N");
            return $@"
{{
    if({arrayName} is null) {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter); goto {endLabel}; }}

    var {arrayCountVar} = {arrayName}.Length;
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.BeginArray, pipeWriter);
    if({arrayCountVar} == 0) {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter); goto {endLabel}; }}

    for(int {indexVar} = 0; {indexVar} < {arrayCountVar} - 1; {indexVar}++)
    {{
        {WriteTypeTemplate(valueType, $"{arrayName}[{indexVar}]")};
        global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.ValueSeparator, pipeWriter);
    }}
    {WriteTypeTemplate(valueType, $"{arrayName}[{arrayCountVar}-1]")};
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter);
    {endLabel}: {{}}
}}";
        }

        private string WriteEnumerableTemplate(Type valueType, string enumerableName)
        {
            var endLabel = "Label" + Guid.NewGuid().ToString("N");
            var notFirstVar = "notFirst" + Guid.NewGuid().ToString("N");
            var itemVar = "item" + Guid.NewGuid().ToString("N");
            return $@"
{{
    if({enumerableName} is null) {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter); goto {endLabel}; }}

    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.BeginArray, pipeWriter);

    bool {notFirstVar} = false;
    foreach(var {itemVar} in {enumerableName})
    {{
        if({notFirstVar})
            global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.ValueSeparator, pipeWriter);
        {WriteTypeTemplate(valueType, itemVar)};
        {notFirstVar} = true;
    }}
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter);

    {endLabel}: {{}}
}}";
        }
    }
}
