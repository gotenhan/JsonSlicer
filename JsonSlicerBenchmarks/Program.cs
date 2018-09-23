using System;
using BenchmarkDotNet.Running;

namespace JsonSlicerBenchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<Benchmarks>();
        }
    }
}
