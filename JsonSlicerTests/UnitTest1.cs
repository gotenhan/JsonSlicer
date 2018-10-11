using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using JsonSlicer;
using JsonSlicerBenchmarks.Models;
using NUnit.Framework;

namespace JsonSlicerTests
{
    [TestFixture]
    public class UnitTest1
    {
        [Test]
        public async Task Test1()
        {
            var pipe = new Pipe();
            var to = GetTestObject();

            var write = Write(to, pipe);

            var json = await Read(pipe).ConfigureAwait(false);
            await write;


            var actual = json.ToString();
            Assert.AreEqual($@"{{
""String"": ""{new string('ক', 40)}"",
""NullString"": null,
""Decimal"": {decimal.MaxValue},
""Nested"": {{
""Short"": {short.MinValue},
""ArrayBytes"": [1,2,3],
""ListFloats"": [1,2,3,-4.99999,{float.MaxValue.ToString(CultureInfo.InvariantCulture)},{float.MinValue.ToString(CultureInfo.InvariantCulture)}]
}},
""Double"": {double.MaxValue.ToString(CultureInfo.InvariantCulture)},
""NullNested"": null,
""ArrayList"": [1,""bla"",{{
""SmallNested"": 1,
""BoolTrue"": true,
""BoolFalse"": false
}}]
}}", actual);
        }

        [Test]
        public async Task Profile()
        {
            var pipe = new Pipe();
            var to = GetTestObject(false);
            var write = Write(to, pipe, 100000);
            var h = await Read(pipe).ConfigureAwait(false);
            await write;
            Assert.Greater(h.ToString().Length, 1);
        }

        [Test]
        public async Task RoslynGeneration()
        {
            Pipe pipe = new Pipe();
            var writer = new JsonWriterGenerator().Generate<NestedB>();
            var readTask = Read(pipe);
            await writer.Write(null, pipe.Writer);
            pipe.Writer.Complete();
            var json = await readTask;
            Assert.AreEqual("test", json);

        }

        private static TestObj GetTestObject(bool arrayList = true)
        {
            var to = new TestObj()
            {
                String = new string('ক', 40),
                Decimal = decimal.MaxValue,
                Double = double.MaxValue,
                Nested = new Nested
                {
                    Short = short.MinValue,
                    ArrayBytes = new byte[] {1, 2, 3 },
                    ListFloats = new List<float> { 1.0f, 2.0f, 3.0f, -4.99999f, float.MaxValue, float.MinValue}
                },
            };
            if (arrayList)
                to.ArrayList = new ArrayList() {1, "bla", new SmallNestedType {SmallNested = 1}};
            return to;
        }

        [Test]
        public async Task Test2()
        {
            var pipe = new Pipe();
            var to = new NestedB();

            var read = Read(pipe);
            await Write(to, pipe).ConfigureAwait(false);

            var json = await read;

            var actual = json.ToString();
            Assert.IsNotNull(actual);
        }

        private static async Task Write<T>(T to, Pipe pipe, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                await TypeSerializer.JsonWriter.WriteObject(to, pipe.Writer).ConfigureAwait(false);
                var fr = await pipe.Writer.FlushAsync().ConfigureAwait(false);
            }

            pipe.Writer.Complete();
        }

        private static async Task<StringBuilder> Read(Pipe pipe)
        {
            ReadResult r;
            StringBuilder json = new StringBuilder();
            while (!r.IsCompleted)
            {
                r = await pipe.Reader.ReadAsync().ConfigureAwait(false);

                foreach (var b in r.Buffer)
                {
                    json.Append(Encoding.UTF8.GetString(b.Span));
                }

                pipe.Reader.AdvanceTo(r.Buffer.End);
            }

            pipe.Reader.Complete();
            return json;
        }

        public class TestObj
        {
            public string String { get; set; }

            public string NullString { get; set; }

            public decimal Decimal { get; set; }

            public Nested Nested { get; set; }

            public double Double { get; set; }

            public Nested NullNested { get; set; }

            public ArrayList ArrayList { get; set; }

        }

        public class Nested
        {
            public short Short { get; set; }

            public byte[] ArrayBytes { get; set; }

            public List<float> ListFloats { get; set; }
        }

        public struct SmallNestedType
        {
            public int SmallNested { get; set; }

            public bool BoolTrue => true;

            public bool BoolFalse => false;
        }
    }
}
