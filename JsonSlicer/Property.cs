using System.Text;

namespace JsonSlicer
{
        public readonly struct Property
        {
            public readonly Token QuotedPropertyNameWithSeparator;

            public Property(string name)
            {
                var Value = Encoding.UTF8.GetBytes(name);
                QuotedPropertyNameWithSeparator = new Token(Token.StringDelimiter,
                    new Token(Value),
                    Token.StringDelimiter,
                    Token.NameSeparator,
                    Token.Space);
            }
        }
}