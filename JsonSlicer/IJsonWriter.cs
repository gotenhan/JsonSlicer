using System.IO.Pipelines;
using System.Threading.Tasks;

namespace JsonSlicer
{
    public interface IJsonWriter
    {
        ValueTask Write(object o, PipeWriter writer);
    }

    public interface IJsonWriter<T> : IJsonWriter
    {
        ValueTask Write(T t, PipeWriter writer);
    }
}