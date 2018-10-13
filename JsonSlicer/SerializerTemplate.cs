using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace JsonSlicer
{
    public  class SerializerTemplate
    {
        private IEnumerable<PropertyInfo> Properties { get; }
        private MethodInfo CastObjectMethod { get; }

        public string SerializerName { get; }
        public Type SerializedType { get; }
        public string SerializedTypePath { get; }

        private static readonly Type[] KnownTypes = new[]
        {
            typeof(string), typeof(decimal), typeof(double), typeof(float), typeof(int), typeof(long), typeof(short),
            typeof(byte), typeof(bool)
        };

        public SerializerTemplate(Type serializedType)
        {
            SerializedType = serializedType;
            unchecked
            {
                var hashCode = serializedType.FullName.Aggregate(1, (h, c) => h * 397 + c);
                SerializerName = $"JsonWriter_{serializedType.Name.Replace("[]", "__ARRAY__")}_{2L*Math.Abs(hashCode)}";
            }
            SerializedTypePath = "global::" + GetNestedTypePath(serializedType);
            Properties = SerializedType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).ToArray();
            CastObjectMethod = typeof(SerializerTemplate).GetMethod(nameof(CastObject), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private string GetNestedTypePath(Type type)
        {
            if (type.IsNested)
            {
                return GetNestedTypePath(type.DeclaringType) + "." + type.Name;
            }
            else
            {
                return type.FullName;
            }
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
		if(obj?.GetType() == typeof({SerializedTypePath}))
			global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(({SerializedTypePath})obj, pipeWriter);
		else
			throw new System.ArgumentException($""Expected obj to be of type { SerializedType.FullName } but received {{obj.GetType()}}"");
        return default; 
    }}

    public global::System.Threading.Tasks.ValueTask Write({SerializedTypePath} obj, System.IO.Pipelines.PipeWriter pipeWriter)
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
                return $@"global::JsonSlicer.JsonPrimitiveWriter.Instance.Write({value}, pipeWriter); return default;";
            }else if (type.IsArray)
            {
                var valueType = type.GetElementType();
                if (KnownTypes.Contains(valueType))
                {
                    return WriteArrayTemplate(valueType, value);
                }
            }
            return "return default;";
        }

        private string WriteArrayTemplate(Type valueType, string arrayName)
        {
            return $@"
{{
    if({arrayName} is null) {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter); return default; }}

    var arrayCount = {arrayName}.Length;
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.BeginArray, pipeWriter);
    if(arrayCount == 0) {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter); return default; }}

    for(int i = 0; i < arrayCount - 1; i++)
    {{
        {WriteTypeTemplate(valueType, $"{arrayName}[i]")};
        global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.ValueSeparator, pipeWriter);
    }}
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter);
    return default;
}}";
        }
    }
}
