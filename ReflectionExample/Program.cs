using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ReflectionExample
{
    public static class Program
    {
        static PropertyInfo[]? stProps;
        static void Main(string[] args)
        {
            UnknownType tt = new UnknownType { Prop1 = "Soy una prop", Prop2 = "Soy otra prop" };
            Random rnd = new Random();

            Stopwatch sw = Stopwatch.StartNew();

            for (int buc = 0; buc < 1000000; buc++)
            {
                tt.Prop1 = rnd.Next().ToString();

                var result = tt.Prop1.ToString() + tt.Prop2.ToString();
            }

            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds.ToString());

            sw = Stopwatch.StartNew();

            for (int buc = 0; buc < 1000000; buc++)
            {
                tt.Prop1 = rnd.Next().ToString();
                var result = SlowReflection(tt);
            }

            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds.ToString());
            
            InitializeFastReflection(tt.GetType());

            sw = Stopwatch.StartNew();

            for (int buc = 0; buc < 1000000; buc++)
            {
                tt.Prop1 = rnd.Next().ToString();
                var result = FastReflection(tt);
            }

            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds.ToString());
            
            var cAnalyzer = CreateAnalyzerByCompileAndReflection(typeof(UnknownType));

            sw = Stopwatch.StartNew();

            for (int buc = 0; buc < 1000000; buc++)
            {
                tt.Prop1 = rnd.Next().ToString();
                var result = cAnalyzer.Analyze(tt);
            }

            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds.ToString());
            
            var eAnalyzer = CreateAnalyzerByExpressionsAndReflection(typeof(UnknownType));

            sw = Stopwatch.StartNew();

            for (int buc = 0; buc < 1000000; buc++)
            {
                tt.Prop1 = rnd.Next().ToString();
                var result = eAnalyzer(tt);
            }

            sw.Stop();
            Console.WriteLine(sw.ElapsedMilliseconds.ToString());

        }


        static string SlowReflection(object ObjectToAnalyze)
        {
            var type = ObjectToAnalyze.GetType();
            var typeInfo = type.GetTypeInfo();
            var props = typeInfo.GetRuntimeProperties();

            string result = "";

            foreach (var prop in props)
            {
                result += prop.GetValue(ObjectToAnalyze).ToString();
            }

            return result;
        }

        static void InitializeFastReflection(Type typeToAnalyze)
        {
            var typeInfo = typeToAnalyze.GetTypeInfo();
            stProps = typeInfo.GetRuntimeProperties().ToArray();
        }

        static string FastReflection(object ObjectToAnalyze)
        {
            string result = "";

            foreach (var prop in stProps)
            {
                result += prop.GetValue(ObjectToAnalyze).ToString();
            }

            return result;
        }

        static IAnalyzeType CreateAnalyzerByCompileAndReflection(Type typeToAnalyze)
        {
            var typeInfo = typeToAnalyze.GetTypeInfo();
            var props = typeInfo.GetRuntimeProperties();

            StringBuilder propCode = new StringBuilder();

            foreach (var prop in props)
            {
                propCode.AppendLine($"            result += typedObj.{prop.Name}.ToString();");
            }

            string code = $@"
using System;
using ReflectionExample;

namespace DynamicAnalyzers
{{
    public class {typeToAnalyze.Name}Analyzer : IAnalyzeType
    {{
        public string Analyze (object objectToAnalyze)
        {{

            var typedObj = ({typeToAnalyze.FullName})objectToAnalyze;
            string result = """";

{propCode.ToString()}

            return result;

        }}
    }}
}}

";

            var analyzedCode = SourceText.From(code);
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);

            var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(analyzedCode, options);

            var references = new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Program).Assembly.Location),
            };

            var compilation = CSharpCompilation.Create("Analyzer.dll",
                new[] { parsedSyntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default));

            MemoryStream ms = new MemoryStream();

            var result = compilation.Emit(ms);

            if (!result.Success)
                throw new InvalidDataException();

            ms.Seek(0, SeekOrigin.Begin);

            var assembly = Assembly.Load(ms.ToArray());
            var types = assembly.GetTypes();
            return (IAnalyzeType)Activator.CreateInstance(types.First());
        }

        static Func<object, string> CreateAnalyzerByExpressionsAndReflection(Type typeToAnalyze)
        {
            var typeInfo = typeToAnalyze.GetTypeInfo();
            var props = typeInfo.GetRuntimeProperties();

            List<Expression> propExprs = new List<Expression>();

            var inputExpression = Expression.Variable(typeof(object));
            var castExpression = Expression.Convert(inputExpression, typeToAnalyze);
            
            var toStrMethodInfo = typeof(object).GetTypeInfo().GetMethod("ToString");
            var strConcat = typeof(string).GetTypeInfo().GetMethod("Concat", new Type[]{ typeof(string), typeof(string) });

            foreach (var prop in props)
            {
                var propExpr = Expression.Property(castExpression, prop.Name);
                var toStrExpr = Expression.Call(propExpr, toStrMethodInfo);
                propExprs.Add(toStrExpr);
            }

            Expression? finalExpression = null;

            foreach (var exp in propExprs)
            {
                if (finalExpression == null)
                    finalExpression = exp;
                else
                    finalExpression = Expression.Call(strConcat, finalExpression, exp);
            }

            if (finalExpression == null)
                throw new InvalidDataException();

            var lambda = Expression.Lambda<Func<object, string>>(finalExpression, inputExpression);
            var compiled = lambda.Compile();

            return compiled;
        }
    }

    public interface IAnalyzeType
    {
        string Analyze (object objectToAnalyze);
    }
}