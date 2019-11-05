using System;
using System.Buffers;
using System.Linq;
using System.Text;

namespace JsonSlicer
{
        public readonly struct Token
        {
            public readonly byte[] Value;

            public Token(byte value)
            {
                Value = new[] {value};
            }

            public Token(byte[] value)
            {
                Value = value;
            }

            public Token(params Token[] values)
            {
                Value = values.SelectMany(v => v.Value).ToArray();
            }

            public static readonly Token BeginObject = new Token(0x7B);
            public static readonly Token EndObject = new Token(0x7D);
            public static readonly Token BeginArray = new Token(0x05B);
            public static readonly Token EndArray = new Token(0x05D);
            public static readonly Token ValueSeparator = new Token(0x2C);
            public static readonly Token NameSeparator = new Token(0x3A);
            public static readonly Token Space = new Token(0x20);
            public static readonly Token HorizontalTab = new Token(0x09);
            public static readonly Token LineFeed = new Token(0x0A);
            public static readonly Token CarriageReturn = new Token(0x0D);
            public static readonly Token NewLine = new Token(Encoding.UTF8.GetBytes(Environment.NewLine));

            public static readonly Token StringDelimiter = new Token(0x22);
            public static readonly Token False = new Token(new byte[] {0x66, 0x61, 0x6C, 0x73, 0x65});
            public static readonly Token True = new Token(new byte[] {0x74, 0x72, 0x75, 0x65});
            public static readonly Token Null = new Token(new byte[] {0x6E, 0x75, 0x6C, 0x6C});
            public static readonly Token EmptyObject = new Token(new byte[] { 0x7B, 0x7D });
        }
}