using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace JsonSlicerBenchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<Benchmarks>(ManualConfig.Create(DefaultConfig.Instance).KeepBenchmarkFiles(true));
        }
    }
}
