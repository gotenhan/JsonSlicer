using System;
using System.IO;
using Newtonsoft.Json;

namespace JsonSlicerBenchmarks
{
    public class NewtonsoftJSerializer: IJSerializer
    {
        public byte[] Serialize(Type t, object o)
        {
            using (var ms = new MemoryStream())
            using(var sw = new StreamWriter(ms))
            using(var jw = new JsonTextWriter(sw))
            {
                var js = new JsonSerializer();
                js.Serialize(jw, o, t);
                return ms.ToArray();
            }
        }

        public override string ToString()
        {
            return "newtonsoft";
        }
    }
}