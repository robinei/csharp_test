using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Core;
using Test.Json;

namespace Test
{
    class Test : IToJson
    {
        public void ToJson(JsonGen gen)
        {
            gen.ArrayBegin();
            gen.Value(666);
            gen.Value("jalla");
            gen.ArrayEnd();
        }
    }

    class MainClass
    {
        public static void Main(string[] args)
        {
            /*var jsonString = File.ReadAllText("sample.json");
            Console.WriteLine("Read json data: " + jsonString.Length + " bytes");

            const int iterations = 50;

            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                for (int i = 0; i < iterations; ++i) {
                    var root = JsonValue.Parse(jsonString);
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


            /*var arr = new int[] { 1, 2, 3 };
            var gen = new JsonGen(true);
            gen.JsonValue((object)arr);
            Console.WriteLine(gen.ToString());*/

            var dict = new Dictionary<string, object> {
                {"foo", 123},
                {"bar", new [] {1, 2, 3 }},
                {"baz", new Test()}
            };
            object obj = (object)dict;
            var gen = new JsonGen(true);
            gen.Value(obj);
            Console.WriteLine(gen.ToString());

            /*short num = 1234;
            object obj = num;
            Console.WriteLine("" + Convert.ToInt64(obj));*/

            //var root = parser.LastParsedRoot;
            //Console.WriteLine(root);

            /*var tokenizer = new JsonTokenizer();
            if (tokenizer.Feed("{\"test\\u20ACas\\t\\tdf\":[true,1,false, null, -123, 453.234, 1.0e1, {\"foo\":1, \"bar\":2}, {}, [], [213]]}")) {
                var parser = new JsonParser();
                if (parser.Feed(tokenizer)) {
                    var root = parser.LastParsedRoot;
                    Console.WriteLine(root.ToString());

                    var gen = new JsonGen(true);
                    gen.JsonValue(root);

                    Console.WriteLine(gen.ToString());
                }
            }*/


            /*var gen = new JsonGen(true);

            gen.JsonValue(new Dictionary<string, int> {
                {"foo", 123},
                {"bar", 123},
                {"baz", 123}
            });

            Console.WriteLine(gen.ToString());*/
        }
    }
}
