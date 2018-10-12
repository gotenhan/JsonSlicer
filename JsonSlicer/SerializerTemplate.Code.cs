using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace JsonSlicer
{
    public partial class SerializerTemplate
    {
        private string SerializerName { get; }
        private Type SerializedType { get; }
        private IEnumerable<PropertyInfo> Properties { get; }
        private MethodInfo CastObjectMethod { get; }

        private static readonly Type[] KnownTypes = new[]
        {
            typeof(string), typeof(decimal), typeof(double), typeof(float), typeof(int), typeof(long), typeof(short),
            typeof(byte), typeof(bool)
        };

        public SerializerTemplate(Type serializedType)
        {
            SerializedType = serializedType;
            SerializerName = $"JsonWriter_{serializedType.Name}_{Math.Abs(serializedType.GetHashCode())}";
            Properties = SerializedType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).ToArray();
            CastObjectMethod = typeof(SerializerTemplate).GetMethod(nameof(CastObject), BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private T CastObject<T>(object obj)
        {
            return (T)obj;	
        }
    }
}
