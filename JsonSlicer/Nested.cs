namespace JsonSlicer.Models
{
    public class NestedC
    {
        public class A
        {
            public string AString { get; set; } = "AString";
            public int AInt { get; set; } = 123141;
            public double ADouble { get; set; } = 123131.1231321; 
        }

        public class B
        {
            public string BString { get; set; } = "Bstring";
            public int BInt { get; set; } = 8821;
            public double BDouble { get; set; } = 82121.1212;
        }

        public class C
        {
            public string CString { get; set; } = "CString";
            public int CInt { get; set; } = 1231;
            public double CDouble { get; set; } = 222.2121;
            public A Ai { get; set; } = new A();
            public B Bi { get; set; } = new B();
        }
        public class D
        {
            public string DString { get; set; } = "DString";
            public int DInt { get; set; } = 44452812;
            public double DDouble { get; set; } = double.MinValue;
            public A Ai { get; set; } = new A();
            public B Bi { get; set; } = new B();
            public C Ci { get; set; } = new C();
        }

        public A Ai { get; set; } = new A();
        public B Bi { get; set; } = new B();
        public C Ci { get; set; } = new C();
        public D Di { get; set; } = new D();

        public override string ToString()
        {
            return nameof(NestedC);
        }
    }
}