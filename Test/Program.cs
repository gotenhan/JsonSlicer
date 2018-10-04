using System;
using System.IO.Pipelines;
using System.Threading.Tasks;
using JsonSlicer;
using JsonSlicer.Models;
using NUnit.Framework;

namespace Test
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Pipe pipe = new Pipe();
            var writer = new JsonWriterGenerator().Generate<NestedC>();
            await writer.Write(null, pipe.Writer);
            //var json = await Read(pipe); Assert.AreEqual("test", json);
            Console.ReadLine();

        }
    }
}
