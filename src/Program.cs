using CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UnityPrecompiler
{
    public class Program
    {
        public const string localFileID = "11500000";
        public const string assemblyMapExt = ".map";

        private static CSharpParseOptions csParseOptions;
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

        public class CopyFlags
        {
            [Option('s', Required = true, HelpText = "Path to source project directory")]
            public string SrcPath { get; set; }

            [Option('d', Required = true, HelpText = "Path to destination project directory")]
            public string DstPath { get; set; }

            public static void Usage()
            {
                Console.WriteLine("Usage: UnityPrecompiler.exe copy srcPath dstPath");
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

            [Option('p', Required = false, Default = "Plugins", HelpText = "Plugin Directory relative to Assets directory in destination project directory")]
            public string PluginsDir { get; set; }

            [Option("defines", Required = false, Default = "", HelpText = "Optional preprocessor defines used to determine class info. Space separated, e.g: \"UNITY_EDITOR UNITY_WSA\") ")]
            public string Defines { get; set; }

            [Option('c', Required = false, Default = "Debug", HelpText = "Configuration to build assemblies (Debug/Release)")]
            public string Configuration { get; set; }

            public static void Usage()
            {
                Console.WriteLine("Usage: UnityPrecompiler.exe compile -s srcPath -d dstPath [-Defines defines] [-c configuration] [-p pluginDir]");
                Console.WriteLine(" - srcPath: path to source project directory");
                Console.WriteLine(" - dstPath: path to target project directory");
                Console.WriteLine(" - pluginDir: Plugin Directory relative to Assets directory in destination project directory");
                Console.WriteLine(" - defines: preprocessor defines used to determine class info");
                Console.WriteLine(" - configuration: Configuration to build assemblies (Debug/Release)");
            }
        }

        public class FixupFlags
        {
            [Option('d', Required = true, HelpText = "Path to destination project directory")]
            public string DstPath { get; set; }

            [Option('p', Required = false, Default = "Plugins", HelpText = "Plugin Directory relative to Assets directory in destination project directory")]
            public string PluginDir { get; set; }

            [Option('x', Required = false, HelpText = "Optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"")]
            public string Extensions { get; set; }

            public static void Usage()
            {
                Console.WriteLine("Usage: UnityPrecompiler.exe fixup -d dstPath [-x extensions] [-p pluginDir]");
                Console.WriteLine(" - dstPath: path to target project directory");
                Console.WriteLine(" - extensions: optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"");
                Console.WriteLine(" - pluginDir: Plugin Directory relative to Assets directory in destination project directory");
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

            [Option('p', Required = false, Default = "Plugins", HelpText = "Plugin Directory relative to Assets directory in destination project directory")]
            public string PluginDir { get; set; }

            [Option('x', Required = false, HelpText = "Optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"")]
            public string Extensions { get; set; }

            public static void Usage()
            {
                Console.WriteLine("Usage: UnityPrecompiler.exe compile -s srcPath -d dstPath [-Defines defines] [-c configuration] [-x extensions] [-p pluginDir]");
                Console.WriteLine(" - srcPath: path to source project directory");
                Console.WriteLine(" - dstPath: path to target project directory");
                Console.WriteLine(" - defines: preprocessor defines used to determine class info");
                Console.WriteLine(" - configuration: Configuration to build assemblies (Debug/Release)");
                Console.WriteLine(" - pluginDir: Plugin Directory relative to Assets directory in destination project directory");
                Console.WriteLine(" - extensions: optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"");
            }
        }

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
                    return Parser.Default.ParseArguments<CopyFlags>(actionArgs)
                        .WithNotParsed(flags => CopyFlags.Usage())
                        .WithParsed(flags => Copy(flags));
                }
                else if (action == "compile")
                {
                    return Parser.Default.ParseArguments<CompileFlags>(actionArgs)
                        .WithNotParsed(flags => CompileFlags.Usage())
                        .WithParsed(flags => Compile(flags));
                }
                else if (action == "fixup")
                {
                    return Parser.Default.ParseArguments<FixupFlags>(actionArgs)
                        .WithNotParsed(flags => FixupFlags.Usage())
                        .WithParsed(flags => Fixup(flags));
                }
                else if (action == "all")
                {
                    return Parser.Default.ParseArguments<AllFlags>(actionArgs)
                        .WithNotParsed(flags => AllFlags.Usage())
                        .WithParsed(flags => All(flags));
                }
            }

            Console.WriteLine("Usage: UnityPrecompiler.exe [all|copy|compile|fixup] -?");
            return null;
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
                Defines = flags.Defines,
                PluginsDir = flags.PluginDir
            };

            var fixupFlags = new FixupFlags
            {
                DstPath = flags.DstPath,
                Extensions = flags.Extensions,
                PluginDir = flags.PluginDir
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
            var dstPluginsDir = Path.Combine(dstAssetsDir, flags.PluginDir?.Length > 0 ? flags.PluginDir : "Plugins");

            if (!Directory.Exists(dstAssetsDir))
            {
                throw new Exception("First argument must be the target project directory (should ");
            }

            if (!Directory.Exists(dstPluginsDir))
            {
                Directory.CreateDirectory(dstPluginsDir);
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
            Console.ForegroundColor = ConsoleColor.Gray;
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
            Console.WriteLine($"Starting copy...");

            using (var wait = new WaitForProcesses())
            {
                wait.Add(MirrorAssets(srcAssetsDir, dstAssetsDir));

                if (hasProjectSettingsDir)
                {
                    wait.Add(MirrorDirectory(srcProjectSettingsDir, dstProjectSettingsDir));
                }

                if (hasPackagesDir)
                {
                    wait.Add(MirrorDirectory(srcPackagesDir, dstPackagesDir));
                }
            }

            Console.WriteLine($"Copy Complete.");
        }

        private static Process MirrorAssets(string from, string to) => ProcessUtil.StartHidden("robocopy", $"\"{from}\" \"{to}\" /XF *.cs *.cs.meta *.asmdef *.asmdef.meta /MIR");
        private static Process MirrorDirectory(string from, string to) => ProcessUtil.StartHidden("robocopy", $"\"{from}\" \"{to}\" /MIR");

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

            var dstPluginsDir = Path.Combine(dstDir, "Assets", flags.PluginsDir);
            if (!Directory.Exists(dstPluginsDir))
            {
                Directory.CreateDirectory(dstPluginsDir);
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
                .AsParallel()
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

            var lockObj = new object();

            // calculate fileIds/guids
            Parallel.ForEach(assemblies, assembly =>
            {
                var hasWarnings = false;

                var sb = new StringBuilder();
                sb.Append($"Processing {assembly.name}...");

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

                    if (!TryGetClassFullName(csPath, csName, sb, ref hasWarnings, out var csClassNamespace, out var csClassName))
                    {
                        return;
                    }

                    file.className = csClassName;
                    file.classNamespace = csClassNamespace;
                    file.classFullName = (csClassNamespace?.Length > 0) ? $"{csClassNamespace}.{csClassName}" : csClassName;

                    file.originalGuid = guid;
                    file.fileID = FileIDUtil.Compute(csClassNamespace, csClassName);
                });

                // remove files without guid
                assembly.files = assembly.files.Where(f => f.originalGuid != null).ToList();

                if (!hasWarnings)
                {
                    sb.AppendLine("OK");
                }

                lock (lockObj)
                {
                    Console.ForegroundColor = hasWarnings ? ConsoleColor.Yellow : ConsoleColor.Gray;
                    Console.Write(sb.ToString());
                }
            });

            using (var procs = new WaitForProcesses())
            {
                // write out assemblies
                Parallel.ForEach(assemblies, assembly =>
                {
                    // Copy assembly over
                    var dstDllPath = Path.Combine(dstPluginsDir, assembly.name + ".dll");
                    File.Copy(assembly.srcDllPath, dstDllPath, overwrite: true);

                    // Copy pdb
                    var dstPdbPath = Path.ChangeExtension(dstDllPath, ".pdb");
                    var srcPdbPath = Path.ChangeExtension(assembly.srcDllPath, ".pdb");
                    File.Copy(srcPdbPath, dstPdbPath, overwrite: true);

                    // Generate mdb from pdb
                    var mdbProcess = CreateMdb(dstDllPath);
                    procs.Add(mdbProcess);

                    // Create meta file
                    var dstAssemblyMetaPath = dstDllPath + ".meta";
                    WriteAssemblyMetaFile(dstAssemblyMetaPath, assembly);

                    // Write map data
                    var dstAssemblyInfoPath = Path.Combine(dstPluginsDir, assembly.name + assemblyMapExt);
                    File.WriteAllText(dstAssemblyInfoPath, JsonConvert.SerializeObject(assembly, Formatting.Indented));
                });
            }
        }

        private static Process CreateMdb(string dstDllPath) => ProcessUtil.StartHidden("pdb2mdb.exe", dstDllPath);

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

            var msbuildPath = ProcessUtil.ExecuteReadOutput("vswhere.exe", @"-latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe")
                .FirstOrDefault();
            if (msbuildPath == null)
            {
                throw new Exception("Failed to find msbuild");
            }

            Console.WriteLine($"Starting compilation...");

            var slnPath = slnPaths[0];
            var build = ProcessUtil.StartHidden(msbuildPath, $"\"{slnPath}\" /t:Build /p:Configuration={configuration}");
            build.WaitForExit();
            if (build.ExitCode != 0)
            {
                throw new Exception($"Build process failed for {slnPath}\n");
            }

            Console.WriteLine($"Compilation complete.");
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
                .Select(csPath => ProcessCsFile(csPath))
                .ToList();

            return assembly;
        }

        private static CsFile ProcessCsFile(string csPath)
        {
            var csMetaPath = csPath + ".meta";
            if (!File.Exists(csMetaPath))
            {
                throw new Exception($"Can't find meta file for {csPath}");

            }

            var file = new CsFile { path = csPath };

            foreach (var line in File.ReadLines(csMetaPath))
            {
                if (line.StartsWith("  executionOrder:"))
                {
                    var value = line.Split(':')[1].Trim();
                    if (!int.TryParse(value, out file.executionOrder))
                    {
                        throw new Exception($"ExecutionOrder in {csPath} is expected to be an integer, was '{value}'");
                    }

                    break;
                }
            }

            return file;
        }

        private static bool TryGetClassFullName(string csPath, string csName, StringBuilder sb, ref bool hasWarnings, out string csClassNamespace, out string csClassName)
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
                        sb.AppendLine("OK With Warnings (Maybe you didn't pass in the right defines?)");
                        hasWarnings = true;
                    }
                    sb.AppendLine($"  - Skipping classless file: {csPath}");
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

            var modifiedExecutionFiles = assembly.files.Where(file => file.executionOrder != 0).ToArray();
            if (modifiedExecutionFiles.Length > 0)
            {
                sb.AppendLine("  executionOrder:");
                foreach (var file in modifiedExecutionFiles)
                {
                    sb.AppendLine($"    {file.classFullName}: {file.executionOrder}");
                }
            }
            else
            {
                sb.AppendLine("  executionOrder: {}");
            }

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
