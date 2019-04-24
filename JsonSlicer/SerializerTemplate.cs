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

        private List<string> SerializedProperties = new List<string>();

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
                var normalizedName = GetNormalizedTypeName(serializedType);
                SerializerName = $"JsonWriter_{normalizedName}_{2L*Math.Abs(hashCode)}";
            }
            SerializedTypePath = "global::" + GetNestedTypePath(serializedType);
        }

        public SerializerResult Generate()
        {
            var referencedTypes = new HashSet<Type>();
            var text = $@"
namespace JsonSlicer.GeneratedSerializers 
{{
  using global::System.Buffers;

  public class {SerializerName} : global::JsonSlicer.IJsonWriter<{SerializedTypePath}>
  {{
    public static {SerializerName} Instance = new {SerializerName}();

    public global::System.Threading.Tasks.ValueTask Write(object obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {{
        if(obj is null)
            {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter); return default; }}
		if(typeof({SerializedTypePath}).IsAssignableFrom(obj.GetType()))
			return Write(({SerializedTypePath})obj, pipeWriter);
		else
			throw new System.ArgumentException($""Expected obj to be of type {SerializedType.FullName} but received {{obj?.GetType()}}"");
    }}

    public async global::System.Threading.Tasks.ValueTask Write({SerializedTypePath} obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {{
		{WriteTypeTemplate(SerializedType, "obj", referencedTypes, topLevel: true)}
    }}

    {WritePropertiesTemplate()}
  }}
}}
";
            // WritePropertiesTemplate has to be at the end, otherwise Properties list will be empty
            // this is so that it doesn't generate Properties array for types with specialized serialization (primitives, arrays, etc)
            return new SerializerResult
            {
                Text = text,
                ReferencedTypes = referencedTypes,
            };
        }

        private string WriteTypeTemplate(Type type, string value, HashSet<Type> referencedTypes, bool topLevel = false)
        {
            if (KnownTypes.Contains(type))
            {
                if (type == typeof(string) || type == typeof(object))
                {
                    return $"if({value} == null) global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter);"+
                           $"else global::JsonSlicer.JsonPrimitiveWriter.Instance.Write({value}, pipeWriter);";
                }

                return $@"global::JsonSlicer.JsonPrimitiveWriter.Instance.Write({value}, pipeWriter);";
            }
            else if (type.IsArray)
            {
                var valueType = type.GetElementType();
                return WriteArrayTemplate(valueType, value, referencedTypes);
            }
            else if (typeof(IEnumerable<>).IsAssignableFrom(type) || typeof(IEnumerable).IsAssignableFrom(type))
            {
                var valueType = type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
                return WriteEnumerableTemplate(valueType, value, referencedTypes);
            }
            else if (type == typeof(object))
            {
                var realTypeVar = "realType" + Guid.NewGuid().ToString("N");
                var serializerVar = "serializer" + Guid.NewGuid().ToString("N");
                return $@"if({value} == null)
{{
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter);
}}
else
{{
    var {realTypeVar} = {value}.GetType();
    if({realTypeVar} == typeof(object))
    {{
        global::JsonSlicer.JsonPrimitiveWriter.Instance.Write({value}, pipeWriter);
    }}
    else
    {{
        var {serializerVar} = global::JsonSlicer.JsonWriterGenerator.Generate({realTypeVar});
        {serializerVar}.Write({value}, pipeWriter);
    }}
}}";
        }
            else
            {
                if (topLevel && type == SerializedType)
                {
                    return WriteObjectTemplate(SerializedType, value, referencedTypes);
                }

                string serializerType;
                if (type == SerializedType)
                {
                    serializerType = SerializerName;
                }
                else
                {
                    var serializer = JsonWriterGenerator.Generate(type);
                    referencedTypes.Add(type);
                    serializerType = GetNestedTypePath(serializer.GetType());
                }

                return $"{serializerType}.Instance.Write({value}, pipeWriter);";
            }
        }

        private string WriteArrayTemplate(Type valueType, string arrayName, HashSet<Type> referencedTypes)
        {
            var endLabel = "Label" + Guid.NewGuid().ToString("N");
            var indexVar = "i" + Guid.NewGuid().ToString("N");
            return $@"
{{
    if({arrayName} is null) {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter); goto {endLabel}; }}

    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.BeginArray, pipeWriter);
    if({arrayName}.Length == 0) {{ global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter); goto {endLabel}; }}

    {WriteTypeTemplate(valueType, $"{arrayName}[0]", referencedTypes)};
    for(int {indexVar} = 1; {indexVar} < {arrayName}.Length; {indexVar}++)
    {{
        global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.ValueSeparator, pipeWriter);
        {WriteTypeTemplate(valueType, $"{arrayName}[{indexVar}]", referencedTypes)};
    }}
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter);
    {endLabel}: {{}}
}}";
        }

        private string WriteEnumerableTemplate(Type valueType, string enumerableName, HashSet<Type> referencedTypes)
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
        {WriteTypeTemplate(valueType, itemVar, referencedTypes)};
        {notFirstVar} = true;
    }}
    global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndArray, pipeWriter);

    {endLabel}: {{}}
}}";
        }

        private string WriteObjectTemplate(Type type, string value, HashSet<Type> referencedTypes)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            var canBeNull = !type.IsValueType || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
            if (canBeNull)
            {
                sb.AppendLine($"if ({value} == null) {{");
                sb.AppendLine("global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.Null, pipeWriter);");
                sb.AppendLine("} else {");
            }

            sb.AppendLine("global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.BeginObject, pipeWriter);");
            sb.AppendLine("global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.NewLine, pipeWriter);");

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => !p.IsSpecialName).ToArray();
            if (properties.Length != 0)
            {
                WriteProperty(0);
                for (int i = 1; i < properties.Length; i++)
                {
                    sb.AppendLine( "global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.ValueSeparator, pipeWriter);");
                    sb.AppendLine("global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.NewLine, pipeWriter);");

                    WriteProperty(i);
                }
            }

            sb.AppendLine("global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.NewLine, pipeWriter);");
            sb.AppendLine("global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Token.EndObject, pipeWriter);");
            if (canBeNull)
            {
                sb.AppendLine("}");
            }

            sb.AppendLine("}");
            return sb.ToString();

            void WriteProperty(int i)
            {
                var prop = properties[i];
                referencedTypes.Add(prop.PropertyType);
                SerializedProperties.Add(prop.Name);

                sb.AppendLine($"global::JsonSlicer.JsonPrimitiveWriter.Instance.Write(Properties[{i}], pipeWriter);");
                sb.AppendLine(WriteTypeTemplate(prop.PropertyType, value + "." + prop.Name, referencedTypes));
            }
        }

        private string WritePropertiesTemplate()
        {
            if (SerializedProperties.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("private global::JsonSlicer.Property[] Properties = {");
                foreach (var prop in SerializedProperties)
                {
                    sb.AppendLine($"new Property(\"{prop}\"),");
                }
                sb.AppendLine("};");
                return sb.ToString();
            }
            else
            {
                return "";
            }
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
    }

    public struct SerializerResult
    {
        public string Text;
        public HashSet<Type> ReferencedTypes;
    }
}
