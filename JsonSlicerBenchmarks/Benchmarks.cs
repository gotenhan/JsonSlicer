using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Xml.XPath;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using BenchmarkDotNet.Validators;
using JsonSlicer;
using JsonSlicerBenchmarks.Models;

namespace JsonSlicerBenchmarks
{
    //[EtwProfiler(false)]
    public class Benchmarks
    {
        [ParamsSource(nameof(Serializers))]
        public object Serializer { get; set; }

        [ParamsSource(nameof(Models))]
        public object Model { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            //var nested = JsonWriterGenerator.Generate<Nested>();
            //var simple = JsonWriterGenerator.Generate<Simple>();
            //Console.WriteLine($"{nested} {simple}");

        }
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
            new JsonSlicerTypeSerializer(),
            new JsonSlicerGeneratedSerializer()
        };

        public IEnumerable<object> Models { get; }= new object[]
        {
            new Simple(),
            new Nested(),
            new LongStrings(),
            Arrays<int>.Random(r => r.Next(), 100, 1337),
            Arrays<int>.Random(r => r.Next(), 10000, 1337),
            Arrays<string>.Random(r => new string((char)r.Next('A', 'z'), r.Next(5, 20)), 100, 256),
            Arrays<string>.Random(r => new string((char)r.Next('A', 'z'), r.Next(5, 20)), 10000, 256),
            Arrays<Simple>.Random(r => new Simple(), 100, 1337),
            Arrays<Nested>.Random(r => new Nested(), 100, 1337),
        };
    }
}
