namespace JsonSlicer.GeneratedSerializers
{
    using System.Buffers;

    public class JsonWriter_NestedB_12345678 : global::JsonSlicer.IJsonWriter<global::JsonSlicer.Models.NestedC>
    {
    public global::System.Threading.Tasks.ValueTask Write(object obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {
        pipeWriter.Write(System.Text.Encoding.UTF8.GetBytes("Writing object of type NestedB"));
        return default;
    }

    public global::System.Threading.Tasks.ValueTask Write(global::JsonSlicer.Models.NestedC obj, System.IO.Pipelines.PipeWriter pipeWriter)
    {
        pipeWriter.Write(System.Text.Encoding.UTF8.GetBytes("Writing object of type NestedB"));
        return default;
    }
    }
}