using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using Test.Json;

namespace Test
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            /*var jsonString = File.ReadAllText("canada.json");
            Console.WriteLine("Read json data: " + jsonString.Length + " bytes");

            const int iterations = 10;

            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                for (int i = 0; i < iterations; ++i) {
                    var tokenizer = new Tokenizer();
                    tokenizer.Tokenize(jsonString);
                    var parser = new Parser();
                    parser.Parse(tokenizer);
                }

                stopwatch.Stop();
                Console.WriteLine("My time elapsed: {0}", stopwatch.Elapsed);
            }

            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                for (int i = 0; i < iterations; ++i) {
                    var root = JObject.Parse(jsonString);
                }

                stopwatch.Stop();
                Console.WriteLine("JSON.Net time elapsed: {0}", stopwatch.Elapsed);
            }*/



            //var root = parser.LastParsedRoot;
            //Console.WriteLine(root);

            var tokenizer = new Tokenizer();
            if (tokenizer.Tokenize("{\"test\\u20ACas\\t\\tdf\":[true,1,false, null, -123, 453.234, 1.0e1, {\"foo\":1, \"bar\":2}, {}, [], [213]]}")) {
                var parser = new Parser();
                if (parser.Parse(tokenizer)) {
                    var root = parser.LastParsedRoot;
                    Console.WriteLine(root.ToString());

                    var gen = new Generator(true);
                    gen.Value(root);

                    Console.WriteLine(gen.ToString());
                }
            }
        }
    }
}
