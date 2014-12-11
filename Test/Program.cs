using System.Diagnostics;
using Test.Json;

namespace Test
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            var tokenizer = new Tokenizer();
            if (tokenizer.Tokenize("{\"test\\u20ACas\\t\\tdf\":[true,1,false, null, -123, 453.234, 1.0e1, {}, [213]]}")) {
                var parser = new Parser();
                if (parser.Parse(tokenizer)) {
                    var root = parser.LastParsedRoot;
                    Debug.WriteLine(root);
                }
            }
        }
    }
}
