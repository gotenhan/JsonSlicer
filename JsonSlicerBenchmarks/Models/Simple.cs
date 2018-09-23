using System;
using System.Collections.Generic;
using System.Text;

namespace JsonSlicerBenchmarks.Models
{
    public class Simple
    {
        public int Int1 { get; set; } = 67943167;
        public int Int2 { get; set; } = int.MaxValue - 1;
        public short Short1 { get; set; } = short.MinValue;
        public short Short2 { get; set; } = 1211;
        public long Long1 { get; set; } = long.MinValue;
        public long Long2 { get; set; } = long.MaxValue;
        public decimal Decimal { get; set; } = decimal.MaxValue;
        public decimal Decimal2 { get; set; } = 43134131412.4143124312412312m;
        public double Double1 { get; set; } = 4114312412412.1234124321;
        public double Double2 { get; set; } = double.MaxValue;
        public float Float1 { get; set; } = float.MaxValue;
        public float Float2 { get; set; } = float.MaxValue / 1.9f;
        public bool Boolean1 { get; set; } = false;
        public bool Boolean2 { get; set; } = true;
        public string String1 { get; set; } = "Some short string";
        public string String2 { get; set; } = "Some even shorter string. hahaha fooled you 12314141t51445132 jklfdas fkadsl; ";

        public override string ToString()
        {
            return nameof(Simple);
        }
    }
}