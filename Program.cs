using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.Contracts;
using System.Text;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CommandLine;
using System.Threading.Tasks;

namespace Compiler
{
    // Taken from http://www.superstarcoders.com/blogs/posts/md4-hash-algorithm-in-c-sharp.aspx
    // Probably not the best implementation of MD4, but it works.
    public class MD4 : HashAlgorithm
    {
        private uint _a;
        private uint _b;
        private uint _c;
        private uint _d;
        private uint[] _x;
        private int _bytesProcessed;

        public MD4()
        {
            _x = new uint[16];

            Initialize();
        }

        public override void Initialize()
        {
            _a = 0x67452301;
            _b = 0xefcdab89;
            _c = 0x98badcfe;
            _d = 0x10325476;

            _bytesProcessed = 0;
        }

        protected override void HashCore(byte[] array, int offset, int length)
        {
            ProcessMessage(Bytes(array, offset, length));
        }

        protected override byte[] HashFinal()
        {
            try
            {
                ProcessMessage(Padding());

                return new[] { _a, _b, _c, _d }.SelectMany(word => Bytes(word)).ToArray();
            }
            finally
            {
                Initialize();
            }
        }

        private void ProcessMessage(IEnumerable<byte> bytes)
        {
            foreach (byte b in bytes)
            {
                int c = _bytesProcessed & 63;
                int i = c >> 2;
                int s = (c & 3) << 3;

                _x[i] = (_x[i] & ~((uint)255 << s)) | ((uint)b << s);

                if (c == 63)
                {
                    Process16WordBlock();
                }

                _bytesProcessed++;
            }
        }

        private static IEnumerable<byte> Bytes(byte[] bytes, int offset, int length)
        {
            for (int i = offset; i < length; i++)
            {
                yield return bytes[i];
            }
        }

        private IEnumerable<byte> Bytes(uint word)
        {
            yield return (byte)(word & 255);
            yield return (byte)((word >> 8) & 255);
            yield return (byte)((word >> 16) & 255);
            yield return (byte)((word >> 24) & 255);
        }

        private IEnumerable<byte> Repeat(byte value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return value;
            }
        }

        private IEnumerable<byte> Padding()
        {
            return Repeat(128, 1)
               .Concat(Repeat(0, ((_bytesProcessed + 8) & 0x7fffffc0) + 55 - _bytesProcessed))
               .Concat(Bytes((uint)_bytesProcessed << 3))
               .Concat(Repeat(0, 4));
        }

        private void Process16WordBlock()
        {
            uint aa = _a;
            uint bb = _b;
            uint cc = _c;
            uint dd = _d;

            foreach (int k in new[] { 0, 4, 8, 12 })
            {
                aa = Round1Operation(aa, bb, cc, dd, _x[k], 3);
                dd = Round1Operation(dd, aa, bb, cc, _x[k + 1], 7);
                cc = Round1Operation(cc, dd, aa, bb, _x[k + 2], 11);
                bb = Round1Operation(bb, cc, dd, aa, _x[k + 3], 19);
            }

            foreach (int k in new[] { 0, 1, 2, 3 })
            {
                aa = Round2Operation(aa, bb, cc, dd, _x[k], 3);
                dd = Round2Operation(dd, aa, bb, cc, _x[k + 4], 5);
                cc = Round2Operation(cc, dd, aa, bb, _x[k + 8], 9);
                bb = Round2Operation(bb, cc, dd, aa, _x[k + 12], 13);
            }

            foreach (int k in new[] { 0, 2, 1, 3 })
            {
                aa = Round3Operation(aa, bb, cc, dd, _x[k], 3);
                dd = Round3Operation(dd, aa, bb, cc, _x[k + 8], 9);
                cc = Round3Operation(cc, dd, aa, bb, _x[k + 4], 11);
                bb = Round3Operation(bb, cc, dd, aa, _x[k + 12], 15);
            }

            unchecked
            {
                _a += aa;
                _b += bb;
                _c += cc;
                _d += dd;
            }
        }

        private static uint ROL(uint value, int numberOfBits)
        {
            return (value << numberOfBits) | (value >> (32 - numberOfBits));
        }

        private static uint Round1Operation(uint a, uint b, uint c, uint d, uint xk, int s)
        {
            unchecked
            {
                return ROL(a + ((b & c) | (~b & d)) + xk, s);
            }
        }

        private static uint Round2Operation(uint a, uint b, uint c, uint d, uint xk, int s)
        {
            unchecked
            {
                return ROL(a + ((b & c) | (b & d) | (c & d)) + xk + 0x5a827999, s);
            }
        }

        private static uint Round3Operation(uint a, uint b, uint c, uint d, uint xk, int s)
        {
            unchecked
            {
                return ROL(a + (b ^ c ^ d) + xk + 0x6ed9eba1, s);
            }
        }
    }

    public static class FileIDUtil
    {
        public static int Compute(string typeNamespace, string typeName)
        {
            string toBeHashed = "s\0\0\0" + typeNamespace + typeName;

            using (HashAlgorithm hash = new MD4())
            {
                byte[] hashed = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(toBeHashed));

                int result = 0;

                for (int i = 3; i >= 0; --i)
                {
                    result <<= 8;
                    result |= hashed[i];
                }

                return result;
            }
        }
    }

    class VersionDefine
    {
        public string name;
        public string expression;
        public string define;
    }

    class AsmDef
    {
        public string name;
        public List<string> includePlatforms = new List<string>();
        public List<string> excludePlatforms = new List<string>();
        public List<string> defineConstraints = new List<string>();
        public List<VersionDefine> versionDefines = new List<VersionDefine>();
    }

    class CsFile
    {
        public string path;
        public string originalGuid;
        public int fileID;

        [JsonIgnore] public CsAssembly assembly;
    }

    class CsAssembly
    {
        public string name;
        public AsmDef asmdef;
        public string asmDefPath;
        public string srcDllPath;
        public string guid;
        public List<CsFile> files = new List<CsFile>();
    }

    public static class ClassDeclarationSyntaxExtensions
    {
        public const string NESTED_CLASS_DELIMITER = "+";
        public const string NAMESPACE_CLASS_DELIMITER = ".";

        public static bool TryGetFullName(this ClassDeclarationSyntax source, out string classNamespace, out string className)
        {
            Contract.Requires(null != source);

            var items = new List<string>();
            var parent = source.Parent;
            while (parent.IsKind(SyntaxKind.ClassDeclaration))
            {
                var parentClass = parent as ClassDeclarationSyntax;
                Contract.Assert(null != parentClass);
                items.Add(parentClass.Identifier.Text);

                parent = parent.Parent;
            }

            var nameSpace = parent as NamespaceDeclarationSyntax;

            classNamespace = nameSpace?.Name?.ToString();

            var sb = new StringBuilder();
            items.Reverse();
            items.ForEach(i => { sb.Append(i).Append(NESTED_CLASS_DELIMITER); });
            sb.Append(source.Identifier.Text);

            className = sb.ToString();
            return true;
        }
    }

    class Program
    {
        public static HashSet<string> fixupExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".unity",
            ".prefab",
            ".mat",
            ".asset",
            ".cubemap",
            ".flare",
            ".compute",
            ".controller",
            ".anim",
            ".overrideController",
            ".mask",
            ".physicsMaterial",
            ".physicsMaterial2D",
            ".guiskin",
            ".fontsettings"
        };

        public const string localFileID = "11500000";
        public const string assemblyMapExt = ".map";
        private static CSharpParseOptions csParseOptions;

        public class CopyFlags
        {
            [Option('s', Required = true, HelpText = "Path to source project directory")]
            public string SrcPath { get; set; }

            [Option('d', Required = true, HelpText = "Path to destination project directory")]
            public string DstPath { get; set; }

            public static void Usage()
            {
                Console.WriteLine("Usage: Compiler copy srcPath dstPath");
                Console.WriteLine(" - srcPath: path to source project directory");
                Console.WriteLine(" - dstPath: path to target project directory");
            }
        }

        public class CompileFlags
        {
            [Option('s', Required = true, HelpText = "Path to source project directory")]
            public string SrcPath { get; set; }

            [Option('d', Required = true, HelpText = "Path to destination project directory")]
            public string DstPath { get; set; }

            [Option("defines", Required = false, Default = "", HelpText = "Optional preprocessor defines used to determine class info. Space separated, e.g: \"UNITY_EDITOR UNITY_WSA\") ")]
            public string Defines { get; set; }

            [Option('c', Required = false, Default = "Debug", HelpText = "Configuration to build assemblies (Debug/Release)")]
            public string Configuration { get; set; }

            public static void Usage()
            {
                Console.WriteLine("Usage: Compiler compile -s srcPath -d dstPath [-Defines defines] [-c configuration]");
                Console.WriteLine(" - srcPath: path to source project directory");
                Console.WriteLine(" - dstPath: path to target project directory");
                Console.WriteLine(" - defines: preprocessor defines used to determine class info");
                Console.WriteLine(" - configuration: Configuration to build assemblies (Debug/Release)");
            }
        }

        public class FixupFlags
        {
            [Option('d', Required = true, HelpText = "Path to destination project directory")]
            public string DstPath { get; set; }

            [Option('x', Required = false, HelpText = "Optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"")]
            public string Extensions { get; set; }

            public static void Usage()
            {
                Console.WriteLine("Usage: Compiler fixup -d dstPath [-x extensions]");
                Console.WriteLine(" - dstPath: path to target project directory");
                Console.WriteLine(" - extensions: optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"");
            }
        }

        public class AllFlags
        {
            [Option('s', Required = true, HelpText = "Path to source project directory")]
            public string SrcPath { get; set; }

            [Option('d', Required = true, HelpText = "Path to destination project directory")]
            public string DstPath { get; set; }

            [Option("defines", Required = false, Default = "", HelpText = "Optional preprocessor defines used to determine class info. Space separated, e.g: \"UNITY_EDITOR UNITY_WSA\") ")]
            public string Defines { get; set; }

            [Option('c', Required = false, Default = "Debug", HelpText = "Configuration to build assemblies (Debug/Release)")]
            public string Configuration { get; set; }

            [Option('x', Required = false, HelpText = "Optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"")]
            public string Extensions { get; set; }


            public static void Usage()
            {
                Console.WriteLine("Usage: Compiler compile -s srcPath -d dstPath [-Defines defines] [-c configuration] [-x extensions]");
                Console.WriteLine(" - srcPath: path to source project directory");
                Console.WriteLine(" - dstPath: path to target project directory");
                Console.WriteLine(" - defines: preprocessor defines used to determine class info");
                Console.WriteLine(" - configuration: Configuration to build assemblies (Debug/Release)");
                Console.WriteLine(" - extensions: optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"");
            }
        }

        static void Main(string[] args)
        {
            ProcessAction(args);

            try
            {
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.ToString());
                throw;
            }
        }

        private static void ProcessAction(string[] args)
        {
            var action = args[0].ToLower();
            var actionArgs = args.Skip(1);
            if (action == "copy")
            {
                Parser.Default.ParseArguments<CopyFlags>(actionArgs)
                    .WithNotParsed(flags => CopyFlags.Usage())
                    .WithParsed(flags => Copy(flags));
            }
            else if (action == "compile")
            {
                Parser.Default.ParseArguments<CompileFlags>(actionArgs)
                    .WithNotParsed(flags => CompileFlags.Usage())
                    .WithParsed(flags => Compile(flags));
            }
            else if (action == "fixup")
            {
                Parser.Default.ParseArguments<FixupFlags>(actionArgs)
                    .WithNotParsed(flags => FixupFlags.Usage())
                    .WithParsed(flags => Fixup(flags));
            }
            else if (action == "all")
            {
                Parser.Default.ParseArguments<AllFlags>(actionArgs)
                    .WithNotParsed(flags => AllFlags.Usage())
                    .WithParsed(flags => All(flags));
            }
        }

        static void All(AllFlags flags)
        {
            var copyFlags = new CopyFlags
            {
                DstPath = flags.DstPath,
                SrcPath = flags.SrcPath
            };

            var compileFlags = new CompileFlags
            {
                DstPath = flags.DstPath,
                SrcPath = flags.SrcPath,
                Configuration = flags.Configuration,
                Defines = flags.Defines
            };

            var fixupFlags = new FixupFlags
            {
                DstPath = flags.DstPath,
                Extensions = flags.Extensions
            };

            var a = Task.Run(() => CompileSolution(compileFlags));
            var b = Task.Run(() => Copy(copyFlags));
            a.Wait();
            b.Wait();

            ProcessAssemblies(compileFlags);

            Fixup(fixupFlags);

            Console.WriteLine();
            Console.WriteLine("Done!");
        }

        static void Fixup(FixupFlags flags)
        {
            var dstDir = flags.DstPath;
            if (flags.Extensions?.Length > 0)
            {
                fixupExtensions = new HashSet<string>(flags.Extensions.Split(' ').Select(e => $".{e}"), StringComparer.OrdinalIgnoreCase);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Fixup]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"  Fixing up script guid in assets.");
            Console.WriteLine($"  - dstDir: {dstDir}");
            Console.WriteLine();


            var dstAssetsDir = Path.Combine(dstDir, "Assets");

            var dstPluginsDir = Path.Combine(dstAssetsDir, "Plugins");
            if (!Directory.Exists(dstPluginsDir))
            {
                throw new Exception("first argument must be the target project directory (should ");
            }

            var csassemblies = Directory
                .EnumerateFiles(dstPluginsDir, "*" + assemblyMapExt, SearchOption.AllDirectories)
                .Select(path => JsonConvert.DeserializeObject<CsAssembly>(File.ReadAllText(path)))
                .ToList();

            // fix references to parent
            csassemblies.ForEach(a => a.files.ForEach(f => f.assembly = a));

            // make sure we don't have any collisions
            var csfiles = csassemblies.SelectMany(a => a.files).ToList();
            var map = new Dictionary<string, CsFile>();
            foreach (var file in csfiles)
            {
                try
                {
                    map.Add(file.originalGuid, file);
                }
                catch
                {
                    throw new Exception($"Collision: {file.originalGuid}.\nNew: {file.assembly.name} > {file.path}\nOld: {map[file.originalGuid].assembly.name} > {map[file.originalGuid].path}");
                }
            }

            var assets = Directory
                .EnumerateFiles(dstAssetsDir, "*.*", SearchOption.AllDirectories)
                .Where(path => fixupExtensions.Contains(Path.GetExtension(path)));

            var regexPattern = @"\{fileID: ([0-9]+), guid: ([0-9a-f]+), type: 3}";

            foreach (var asset in assets)
            {
                var contents = File.ReadAllText(asset);
                var didReplace = false;
                var replaced = Regex.Replace(contents, regexPattern, match =>
                {
                    var fileID = match.Groups[1].Value;
                    var guid = match.Groups[2].Value;

                    if (fileID == localFileID && map.TryGetValue(guid, out var file))
                    {
                        var newFileID = file.fileID;
                        var newGuid = file.assembly.guid;

                        didReplace = true;
                        return $"{{fileID: {newFileID}, guid: {newGuid}, type: 3}}";
                    }

                    return match.Groups[0].Value;
                });

                if (didReplace)
                {
                    Console.WriteLine($"Fixing up {asset}");
                    File.WriteAllText(asset, replaced);
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Fixup Complete.");
            Console.WriteLine();
        }

        static void Copy(CopyFlags flags)
        {
            var srcProjectDir = flags.SrcPath;
            var dstProjectDir = flags.DstPath;

            var dstAssetsDir = Path.Combine(dstProjectDir, "Assets");
            var srcAssetsDir = Path.Combine(srcProjectDir, "Assets");
            if (!Directory.Exists(srcAssetsDir))
            {
                throw new Exception("first argument must be the unity project directory (one level up from Assets)");
            }

            var dstProjectSettingsDir = Path.Combine(dstProjectDir, "ProjectSettings");
            var srcProjectSettingsDir = Path.Combine(srcProjectDir, "ProjectSettings");
            var hasProjectSettingsDir = Directory.Exists(srcProjectSettingsDir);

            var dstPackagesDir = Path.Combine(dstProjectDir, "Packages");
            var srcPackagesDir = Path.Combine(srcProjectDir, "Packages");
            var hasPackagesDir = Directory.Exists(srcPackagesDir);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Copying]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"  Copying assets to output project directory.");
            Console.WriteLine($"  - srcPath: {srcAssetsDir}");
            Console.WriteLine($"  - dstPath: {dstAssetsDir}");
            Console.WriteLine();
            Console.Write($"Starting copy...");

            var a = Process.Start("robocopy", $"\"{srcAssetsDir}\" \"{dstAssetsDir}\" /XF *.cs *.cs.meta *.asmdef *.asmdef.meta /S /PURGE");
            var b = hasProjectSettingsDir ? Process.Start("robocopy", $"\"{srcProjectSettingsDir}\" \"{dstProjectSettingsDir}\" /MIR") : null;
            var c = hasPackagesDir ? Process.Start("robocopy", $"\"{srcPackagesDir}\" \"{dstPackagesDir}\" /MIR") : null;

            a?.WaitForExit();
            b?.WaitForExit();
            c?.WaitForExit();

            Console.WriteLine($"Copy Complete.");
            Console.WriteLine();
        }

        static void Compile(CompileFlags flags)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Compiling]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($" - srcPath: {flags.SrcPath}");
            Console.WriteLine($" - dstPath: {flags.DstPath}");
            Console.WriteLine($" - defines: {flags.Defines}");
            Console.WriteLine();

            CompileSolution(flags);
            ProcessAssemblies(flags);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Compile Complete.");
            Console.WriteLine();
        }

        private static void ProcessAssemblies(CompileFlags flags)
        {
            var srcProjectDir = flags.SrcPath;
            var dstDir = flags.DstPath;
            var defines = flags.Defines?.Length > 0 ? flags.Defines.Split(' ') : new string[0];
            var configuration = flags.Configuration;

            csParseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(defines);

            var dstPluginsDir = Path.Combine(dstDir, "Assets\\Plugins");
            if (!Directory.Exists(dstPluginsDir))
            {
                Directory.CreateDirectory(dstPluginsDir);
            }

            var dstEditorPluginsDir = Path.Combine(dstPluginsDir, "Editor");
            if (!Directory.Exists(dstEditorPluginsDir))
            {
                Directory.CreateDirectory(dstEditorPluginsDir);
            }

            var srcAssetsDir = Path.Combine(srcProjectDir, "Assets");
            if (!Directory.Exists(srcAssetsDir))
            {
                throw new Exception("first argument must be the unity project directory (one level up from Assets)");
            }

            var srcAssembliesDir = Path.Combine(srcProjectDir, $"Temp\\bin\\{configuration}");
            if (!Directory.Exists(srcAssembliesDir))
            {
                throw new Exception("Precompiled assemblies not available. Open Unity of source project dir to get them compiled first.");
            }

            var assemblies = Directory
                .GetFiles(srcAssetsDir, "*.asmdef", SearchOption.AllDirectories)
                .Select(asmdefPath => ProcessAssembly(asmdefPath, srcAssembliesDir))
                .ToList();

            // determine hierarchy and remove nested
            var asmDirs = assemblies.ToDictionary(a => Path.GetDirectoryName(a.asmDefPath));
            foreach (var pair in asmDirs)
            {
                var childFiles = pair.Value.files.ToDictionary(f => f.path);

                // walk up the hierarchy
                var path = pair.Key;
                while (path.Length > srcAssetsDir.Length)
                {
                    path = Path.GetDirectoryName(path);

                    if (asmDirs.TryGetValue(path, out var parent))
                    {
                        parent.files = parent.files.Where(f => !childFiles.ContainsKey(f.path)).ToList();
                    }
                }
            }

            // calculate fileIds/guids
            assemblies.ForEach(assembly =>
            {
                var hasWarnings = false;

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($"Processing {assembly.name}...");

                assembly.files.ForEach(file =>
                {
                    var csmetaPath = file.path + ".meta";
                    if (!File.Exists(csmetaPath))
                    {
                        throw new Exception($"{csmetaPath} doesn't exist");
                    }

                    var csdata = File.ReadLines(csmetaPath).Take(2).ToArray();
                    if (csdata[0] != "fileFormatVersion: 2")
                    {
                        throw new Exception($"{csmetaPath} is not file format 2");
                    }

                    var csPath = csmetaPath.Substring(0, csmetaPath.Length - 5);
                    var csName = Path.GetFileNameWithoutExtension(csPath);
                    var guid = csdata[1].Split(':')[1].TrimStart();

                    if (!TryGetClassFullName(csPath, csName, ref hasWarnings, out var csClassNamespace, out var csClassName))
                    {
                        return;
                    }

                    file.originalGuid = guid;
                    file.fileID = FileIDUtil.Compute(csClassNamespace, csClassName);
                });

                // remove files without guid
                assembly.files = assembly.files.Where(f => f.originalGuid != null).ToList();

                if (!hasWarnings)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK");
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
            });

            // write out assemblies
            assemblies.ForEach(assembly =>
            {
                // TODO: get from asmdef
                //var isEditorPlugin = assembly.name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) != -1;
                //var pluginDir = isEditorPlugin ? dstEditorPluginsDir : dstPluginsDir;
                var pluginDir = dstPluginsDir;

                // Copy assembly over
                var dstAssemblyPath = Path.Combine(pluginDir, assembly.name + ".dll");
                File.Copy(assembly.srcDllPath, dstAssemblyPath, overwrite: true);

                // Create meta file
                var dstAssemblyMetaPath = dstAssemblyPath + ".meta";
                WriteAssemblyMetaFile(dstAssemblyMetaPath, assembly);

                // Write map data
                var dstAssemblyInfoPath = Path.Combine(dstPluginsDir, assembly.name + assemblyMapExt);
                File.WriteAllText(dstAssemblyInfoPath, JsonConvert.SerializeObject(assembly, Formatting.Indented));
            });

        }

        private static List<string> Execute(string filename, string args)
        {
            var list = new List<string>();
            var process = new Process();
            var startinfo = new ProcessStartInfo(filename, args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            process.StartInfo = startinfo;
            process.OutputDataReceived += (_, a) => list.Add(a.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
            return list;
        }

        private static void CompileSolution(CompileFlags flags)
        {
            var srcPath = flags.SrcPath;
            var configuration = flags.Configuration;

            var slnPaths = Directory.GetFiles(srcPath, "*.sln");
            if (slnPaths.Length == 0)
            {
                throw new Exception("Don't have a solution to build from. Please open the solution from Unity first");
            }
            else if (slnPaths.Length > 1)
            {
                throw new Exception("Multiple solutions found in the project dir. There should only be one.");
            }

            var msbuildPath = Execute("vswhere.exe", @"-latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe")
                .FirstOrDefault();
            if (msbuildPath == null)
            {
                throw new Exception("Failed to find msbuild");
            }

            var slnPath = slnPaths[0];
            var build = Process.Start(msbuildPath, $"\"{slnPath}\" /t:Build /p:Configuration={configuration}");
            build.WaitForExit();

            //if (build.ExitCode != 0)
            //{
            //    throw new Exception($"Build process failed for {slnPath}");
            //}
        }

        private static CsAssembly ProcessAssembly(string asmdefPath, string srcAssembliesDir)
        {
            var asmdefDir = Path.GetDirectoryName(asmdefPath);
            var asmdef = JsonConvert.DeserializeObject<AsmDef>(File.ReadAllText(asmdefPath));

            var srcAssemblyFilename = asmdef.name + ".dll";
            var srcAssemblyPath = Path.Combine(srcAssembliesDir, srcAssemblyFilename);
            if (!File.Exists(srcAssemblyPath))
            {
                throw new Exception($"Assembly '{asmdef.name}' not available (expected path: {srcAssemblyPath}). Open Unity and allow it to compile the source project dir first to make the assemblies available.");
            }

            var assembly = new CsAssembly
            {
                asmdef = asmdef,
                asmDefPath = asmdefPath,
                srcDllPath = srcAssemblyPath,
                name = asmdef.name,
                guid = Guid.NewGuid().ToString().ToLower().Replace("-", "")
            };

            assembly.files = Directory
                .GetFiles(asmdefDir, "*.cs", SearchOption.AllDirectories)
                .Select(csPath => new CsFile { path = csPath })
                .ToList();

            return assembly;
        }

        private static bool TryGetClassFullName(string csPath, string csName, ref bool hasWarnings, out string csClassNamespace, out string csClassName)
        {
            var csTree = CSharpSyntaxTree.ParseText(File.ReadAllText(csPath), csParseOptions);
            var csRoot = (CompilationUnitSyntax)csTree.GetRoot();
            var csClasses = csRoot.DescendantNodes().OfType<ClassDeclarationSyntax>();
            var csClass = csClasses.FirstOrDefault();
            if (csClass == null)
            {
                if (csRoot.DescendantNodes().OfType<StructDeclarationSyntax>().Any() ||
                    csRoot.DescendantNodes().OfType<EnumDeclarationSyntax>().Any() ||
                    csRoot.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Any())
                {
                    // has a struct, enum, interface
                }
                else if (csName == "AssemblyInfo")
                {
                    // known
                }
                else
                {
                    if (!hasWarnings)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("OK With Warnings (Maybe you didn't pass in the right defines?)");
                        hasWarnings = true;
                    }
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  - Skipping classless file: {csPath}");
                }

                csClassNamespace = null;
                csClassName = null;
                return false;
            }

            return csClass.TryGetFullName(out csClassNamespace, out csClassName);
        }

        private static void WriteAssemblyMetaFile(string dstAssemblyMetaPath, CsAssembly assembly)
        {
            var sb = new StringBuilder();
            sb.AppendLine("fileFormatVersion: 2");
            sb.AppendLine($"guid: {assembly.guid}");
            sb.AppendLine("PluginImporter:");
            sb.AppendLine("  externalObjects: {}");
            sb.AppendLine("  serializedVersion: 2");
            sb.AppendLine("  iconMap: {}");
            sb.AppendLine("  executionOrder: {}");

            var defines = assembly.asmdef.versionDefines.Select(d => d.define)
                .Concat(assembly.asmdef.defineConstraints);

            if (defines.Any())
            {
                sb.AppendLine("  defineConstraints:");
                foreach (var define in defines)
                {
                    sb.AppendLine($"    - {define}");
                }
            }
            else
            {
                sb.AppendLine("  defineConstraints: []");
            }

            sb.AppendLine("  isPreloaded: 0");
            sb.AppendLine("  isOverridable: 0");
            sb.AppendLine("  isExplicitlyReferenced: 0");
            sb.AppendLine("  validateReferences: 1");

            var hasIncludeSet = assembly.asmdef.includePlatforms.Count > 0;
            var platformList = hasIncludeSet ? assembly.asmdef.includePlatforms : assembly.asmdef.excludePlatforms;
            var platformSet = new HashSet<string>(platformList);
            var onFlag = hasIncludeSet ? 0 : 1;
            var offFlag = hasIncludeSet ? 1 : 0;

            var excludeEditor = platformSet.Contains("Editor") ? onFlag : offFlag;
            var excludeWsa = platformSet.Contains("WSA") ? onFlag : offFlag;
            var excludeWin32 = platformSet.Contains("WindowsStandalone32") ? onFlag : offFlag;
            var excludeWin64 = platformSet.Contains("WindowsStandalone64") ? onFlag : offFlag;

            var enableEditor = 1 - excludeEditor;
            var enableWsa = 1 - excludeWsa;
            var enableWin32 = 1 - excludeWin32;
            var enableWin64 = 1 - excludeWin64;

            sb.AppendLine("  platformData:");
            sb.AppendLine("  - first:");
            sb.AppendLine("      : Any");
            sb.AppendLine("    second:");
            sb.AppendLine("      enabled: 0");
            sb.AppendLine("      settings:");
            sb.AppendLine($"        Exclude Editor: {excludeEditor}");
            sb.AppendLine($"        Exclude Linux64: 1");
            sb.AppendLine($"        Exclude OSXUniversal: 1");
            sb.AppendLine($"        Exclude Win: {excludeWin32}");
            sb.AppendLine($"        Exclude Win64: {excludeWin64}");
            sb.AppendLine($"        Exclude WindowsStoreApps: {excludeWsa}");

            sb.AppendLine($"  - first:                                   ");
            sb.AppendLine($"      Editor: Editor                         ");
            sb.AppendLine($"    second:                                  ");
            sb.AppendLine($"      enabled: {enableEditor}");
            sb.AppendLine($"      settings:                              ");
            sb.AppendLine($"        CPU: AnyCPU                          ");
            sb.AppendLine($"        DefaultValueInitialized: true        ");
            sb.AppendLine($"        OS: AnyOS                            ");
            sb.AppendLine($"  - first:                                   ");
            sb.AppendLine($"      Standalone: Linux64                    ");
            sb.AppendLine($"    second:                                  ");
            sb.AppendLine($"      enabled: 0                             ");
            sb.AppendLine($"      settings:                              ");
            sb.AppendLine($"        CPU: x86_64                          ");
            sb.AppendLine($"  - first:                                   ");
            sb.AppendLine($"      Standalone: OSXUniversal               ");
            sb.AppendLine($"    second:                                  ");
            sb.AppendLine($"      enabled: 0                             ");
            sb.AppendLine($"      settings:                              ");
            sb.AppendLine($"        CPU: x86_64                          ");
            sb.AppendLine($"  - first:                                   ");
            sb.AppendLine($"      Standalone: Win                        ");
            sb.AppendLine($"    second:                                  ");
            sb.AppendLine($"      enabled: {enableWin32}");
            sb.AppendLine($"      settings:                              ");
            sb.AppendLine($"        CPU: x86                             ");
            sb.AppendLine($"  - first:                                   ");
            sb.AppendLine($"      Standalone: Win64                      ");
            sb.AppendLine($"    second:                                  ");
            sb.AppendLine($"      enabled:  {enableWin64}");
            sb.AppendLine($"      settings:                              ");
            sb.AppendLine($"        CPU: x86_64                          ");
            sb.AppendLine($"  - first:                                   ");
            sb.AppendLine($"      Windows Store Apps: WindowsStoreApps   ");
            sb.AppendLine($"    second:                                  ");
            sb.AppendLine($"      enabled:  {enableWsa}");
            sb.AppendLine($"      settings:                              ");
            sb.AppendLine($"        CPU: AnyCPU                          ");
            sb.AppendLine($"        DontProcess: false                   ");
            sb.AppendLine($"        PlaceholderPath:                     ");
            sb.AppendLine($"        SDK: AnySDK                          ");
            sb.AppendLine($"        ScriptingBackend: AnyScriptingBackend");

            sb.AppendLine("  userData:");
            sb.AppendLine("  assetBundleName:");
            sb.AppendLine("  assetBundleVariant:");

            File.WriteAllText(dstAssemblyMetaPath, sb.ToString());            
        }
    }
}
