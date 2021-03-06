﻿using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using JsonSlicer;

namespace JsonSlicerBenchmarks
{
    public class JsonSlicerTypeSerializer : IJSerializer
    {
        public byte[] Serialize(Type t, object o)
        {
            async Task<byte[]> Read(Pipe pipe2)
            {
                ReadResult r;
                var ms = new MemoryStream();
                do
                {
                    r = await pipe2.Reader.ReadAsync().ConfigureAwait(false);

                    foreach (var b in r.Buffer)
                    {
                        ms.Write(b.Span);
                    }

                    pipe2.Reader.AdvanceTo(r.Buffer.End);
                } while (!r.IsCompleted);

                return ms.ToArray();
            }

            async Task Write(Pipe pipe1)
            {
                await TypeSerializer.JsonWriter.WriteObject(o, pipe1.Writer).ConfigureAwait(false);
                await pipe1.Writer.FlushAsync().ConfigureAwait(false);
                pipe1.Writer.Complete();
            }

            var pipe = new Pipe();

            var readTask = Read(pipe);
            var writeTask = Write(pipe);

            writeTask.GetAwaiter().GetResult();
            return readTask.GetAwaiter().GetResult();
        }

        public override string ToString()
        {
            return "slicer";
        }
    }
}