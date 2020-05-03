using CommandLine;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace UnityPrecompiler
{
    public class Program
    {
        static void Main(string[] args)
        {
            try
            {
                ProcessAction(args);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.ToString());
                Console.ForegroundColor = ConsoleColor.Gray;
                Environment.Exit(1);
            }
        }

        private static object ProcessAction(string[] args)
        {
            if (args.Length > 0)
            {
                var action = args[0].ToLower();
                var actionArgs = args.Skip(1);
                if (action == "copy")
                {
                    return Parser.Default.ParseArguments<Flags>(actionArgs)
                        .WithNotParsed(flags => Flags.Usage())
                        .WithParsed(flags => Copy.Execute(flags));
                }
                else if (action == "compile")
                {
                    return Parser.Default.ParseArguments<Flags>(actionArgs)
                        .WithNotParsed(flags => Flags.Usage())
                        .WithParsed(flags => new Compile(flags).Execute());
                }
                else if (action == "fixup")
                {
                    return Parser.Default.ParseArguments<BaseFlags>(actionArgs)
                        .WithNotParsed(flags => BaseFlags.Usage())
                        .WithParsed(flags => Fixup.Execute(flags));
                }
                else if (action == "all")
                {
                    return Parser.Default.ParseArguments<Flags>(actionArgs)
                        .WithNotParsed(flags => Flags.Usage())
                        .WithParsed(flags => All(flags));
                }
            }

            Console.WriteLine("Usage: UnityPrecompiler.exe [all|copy|compile|fixup] -?");
            return null;
        }

        static void All(Flags flags)
        {
            var compile = new Compile(flags);

            var a = Task.Run(() => compile.CompileProjects());
            var b = Task.Run(() => Copy.Execute(flags));
            a.Wait();
            b.Wait();

            compile.ProcessAssemblies();
            Fixup.Execute(flags);

            Console.WriteLine();
            Console.WriteLine("Done!");
        }

    }
}
