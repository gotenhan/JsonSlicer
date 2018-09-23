using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Xml.XPath;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Validators;
using JsonSlicerBenchmarks.Models;

namespace JsonSlicerBenchmarks
{
    public class Benchmarks
    {
        [ParamsSource(nameof(Serializers))]
        public object Serializer { get; set; }

        [ParamsSource(nameof(Models))]
        public object Model { get; set; }

        [Benchmark]
        [ArgumentsSource(nameof(Serializers))]
        public byte[] Serialize()
        {
            
            var s = (IJSerializer)Serializer;
            var bs = s.Serialize(Model.GetType(), Model);
            return bs;
        }

        public IEnumerable<IJSerializer> Serializers { get; } = new IJSerializer[]
        {
            new NewtonsoftJSerializer(),
            new JsonSlicerSerializer()
        };

        public IEnumerable<object> Models { get; }= new object[]
        {
            new Simple(),
            new Nested()
        };
    }
}
