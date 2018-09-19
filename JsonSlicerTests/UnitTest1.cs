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
            var to = new TestObj()
            {
                String = new string('ক', 15),
                Decimal = decimal.MaxValue,
                Double = double.MaxValue,
                Nested = new Nested
                {
                    Short = short.MinValue,
                    ArrayBytes = new byte[] {1, 2, 3 },
                    ListFloats = new List<float> { 1.0f, 2.0f, 3.0f, -4.99999f, float.MaxValue, float.MinValue}
                },
                ArrayList = new ArrayList() { 1, "bla", new SmallNestedType { SmallNested = 1 } }
            };
            long writingElapsed = 0;
            Task.Run(async () =>
            {
                await TypeSerializer.JsonWriter.WriteAsync(to, pipe.Writer);
                await pipe.Writer.FlushAsync();
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < 1000; i++)
                {
                    await TypeSerializer.JsonWriter.WriteAsync(to, pipe.Writer);
                    await pipe.Writer.FlushAsync();
                }

                pipe.Writer.Complete();

                writingElapsed = stopwatch.ElapsedMilliseconds;
            });

            ReadResult r;
            StringBuilder json = new StringBuilder();
            var ms = new MemoryStream(new byte[4439 * 1000]);
            long readingElapsed = 0;
            {
                var stopwatch = Stopwatch.StartNew();

                do
                {
                    r = await pipe.Reader.ReadAsync();

                    foreach (var b in r.Buffer)
                    {
                        //json.Append(Encoding.UTF8.GetString(b.Span));
                        ms.Write(b.Span);
                    }

                    pipe.Reader.AdvanceTo(r.Buffer.End);
                } while (!(r.IsCompleted && r.Buffer.IsEmpty));

                readingElapsed = stopwatch.ElapsedMilliseconds;
            }

            var actual = Encoding.UTF8.GetString(ms.ToArray());//json.ToString());
            Assert.AreEqual($@"{{
""String"": ""{new string('ক', 4096)}"",
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
}}", actual, $"Writing took {writingElapsed}, reading took {readingElapsed}");
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
