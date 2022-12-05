using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReflectionExample
{
    public class UnknownType
    {
        public string Prop1 { get; set; }
        public string Prop2 { get; set; }

        public bool Func1(int Param1)
        {
            return Math.Sign(Param1) == -1;
        }
    }
}
