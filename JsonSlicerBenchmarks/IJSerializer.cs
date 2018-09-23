using System;

namespace JsonSlicerBenchmarks
{
    public interface IJSerializer
    {
        byte[] Serialize(Type t, object o);
    }
}