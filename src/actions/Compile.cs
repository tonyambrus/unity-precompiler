using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityPrecompiler
{
    public class Compile
    {
        private CSharpParseOptions csParseOptions;
        private Flags flags;
        private List<CsAssembly> assemblies;
        private string srcProjectDir;
        private string dstDir;
        private string[] defines;
        private string configuration;
        private string subDir;
        private string dstPluginsDir;
        private string srcPath;
        private string srcAssembliesDir;

        public Compile(Flags flags)
        {
            this.flags = flags;

            srcProjectDir = @"\\?\" + Path.GetFullPath(flags.SrcPath);
            dstDir = @"\\?\" + Path.GetFullPath(flags.DstPath);
            defines = flags.Defines?.Length > 0 ? flags.Defines.Split(' ') : new string[0];
            subDir = flags.FilterDir?.Length > 0 ? Path.Combine("Assets", flags.FilterDir) : "Assets";
            configuration = flags.Configuration;

            csParseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(defines);

            srcPath = Path.Combine(srcProjectDir, subDir);
            if (!Directory.Exists(srcPath))
            {
                throw new Exception($"Can't find {srcPath}");
            }

            srcAssembliesDir = Path.Combine(srcProjectDir, $"Temp\\bin\\{configuration}");
        }

        public void Execute()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Compiling]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($" - srcPath: {flags.SrcPath}");
            Console.WriteLine($" - dstPath: {flags.DstPath}");
            Console.WriteLine($" - defines: {flags.Defines}");
            Console.WriteLine();

            CompileProjects();
            ProcessAssemblies();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Compile Complete.");
            Console.WriteLine();
        }

        private bool IsPathIgnored(string path)
        {
            // git can't handle the "\\?\" prefix
            path = path.TrimStart('\\','?');

            var ignored = ProcessUtil
                .ExecuteReadOutput("git", $"check-ignore --no-index \"{path}\"", flags.SrcPath)
                .Any(line => line?.Length > 0);

            if (ignored)
            {
                Console.WriteLine($"Ignoring .gitignored asmdef at {path}");
            }

            return ignored;
        }

        private List<CsAssembly> GetAssemblies()
        {
            if (assemblies == null)
            {
                var set = (IEnumerable<string>)Directory
                    .GetFiles(srcPath, "*.asmdef", SearchOption.AllDirectories)
                    .AsParallel();

                if (flags.CheckGitIgnore)
                {
                    set = set.Where(srcPath => !IsPathIgnored(srcPath));
                }

                assemblies = set
                    .Select(asmdefPath => GatherAssemblyInfo(asmdefPath, srcAssembliesDir))
                    .ToList();
            }

            return assemblies;
        }

        internal void ProcessAssemblies()
        {
            var assemblies = GetAssemblies();

            dstPluginsDir = Path.Combine(dstDir, "Assets", flags.PluginsDir);
            if (!Directory.Exists(dstPluginsDir))
            {
                Directory.CreateDirectory(dstPluginsDir);
            }

            // determine hierarchy and remove nested
            var asmDirs = assemblies.ToDictionary(a => Path.GetDirectoryName(a.asmDefPath));
            foreach (var pair in asmDirs)
            {
                var childFiles = pair.Value.files.ToDictionary(f => f.path);

                // walk up the hierarchy
                var path = pair.Key;
                while (path.Length > srcPath.Length)
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
                    var dstAssemblyInfoPath = Path.Combine(dstPluginsDir, assembly.name + Constants.AssemblyMapExt);
                    File.WriteAllText(dstAssemblyInfoPath, JsonConvert.SerializeObject(assembly, Formatting.Indented));
                });
            }
        }

        private Process CreateMdb(string dstDllPath) => ProcessUtil.StartHidden("pdb2mdb.exe", dstDllPath);

        private string GetMsbuildPath()
        {
            var msbuildPath = ProcessUtil
                .ExecuteReadOutput("vswhere.exe", @"-latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe")
                .FirstOrDefault();

            if (msbuildPath == null)
            {
                throw new Exception("Failed to find msbuild");
            }

            return msbuildPath;
        }

        internal void CompileProjects()
        {
            var assemblies = GetAssemblies();

            var msbuildPath = GetMsbuildPath();
            var workingDir = flags.SrcPath;

            void compile(CsAssembly assembly)
            {
                var csprojPath = Path.Combine(flags.SrcPath, assembly.name + ".csproj");
                if (!File.Exists(csprojPath))
                {
                    throw new Exception($"Can't find {csprojPath}");
                }

                var logPath = Path.Combine(workingDir, $"{assembly.name}.log");
                var build = ProcessUtil.StartHidden(msbuildPath, $"\"{csprojPath}\" /t:Build /p:Configuration={configuration} /fl \"/flp:logfile={logPath}\"", workingDir);
                build.WaitForExit();

                if (build.ExitCode != 0)
                {
                    throw new Exception($"Build process failed for {csprojPath}.\nLogfile: {logPath}");
                }

                if (!File.Exists(assembly.srcDllPath))
                {
                    throw new Exception($"Expected '{assembly.name}' to have compiled dll {assembly.srcDllPath}, but not available.");
                }
            }

            // Try to run fast in parallel
            var failed = new List<CsAssembly>();
            Parallel.ForEach(assemblies, assembly =>
            {
                try
                {
                    compile(assembly);
                    Console.WriteLine($"Compiled {assembly.name}");
                }
                catch
                {
                    lock (failed)
                    {
                        failed.Add(assembly);
                    }
                }
            });

            // if we failed any, try rerunning sequentially
            failed.ForEach(assembly =>
            {
                compile(assembly);
                Console.WriteLine($"Compiled {assembly.name}");
            });
        }

        private CsAssembly GatherAssemblyInfo(string asmdefPath, string srcAssembliesDir)
        {
            var asmdefDir = Path.GetDirectoryName(asmdefPath);
            var asmdef = JsonConvert.DeserializeObject<AsmDef>(File.ReadAllText(asmdefPath));

            var srcAssemblyFilename = asmdef.name + ".dll";
            var srcAssemblyPath = Path.Combine(srcAssembliesDir, srcAssemblyFilename);

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

        private CsFile ProcessCsFile(string csPath)
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

        private bool TryGetClassFullName(string csPath, string csName, StringBuilder sb, ref bool hasWarnings, out string csClassNamespace, out string csClassName)
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

        private void WriteAssemblyMetaFile(string dstAssemblyMetaPath, CsAssembly assembly)
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
