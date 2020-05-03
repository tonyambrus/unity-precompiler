using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnityPrecompiler
{
    public class Fixup
    {
        public static void Execute(Flags flags)
        {
            var dstDir = flags.DstPath;
            var exts = (flags.Extensions?.Length > 0) ? flags.Extensions.Split(' ').Select(e => $".{e}") : Constants.FixupExtensions;
            var fixupExtensions = new HashSet<string>(exts, StringComparer.OrdinalIgnoreCase);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Fixup]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"  Fixing up script guid in assets.");
            Console.WriteLine($"  - dstDir: {dstDir}");
            Console.WriteLine();

            var dstAssetsDir = Path.Combine(dstDir, "Assets");
            var dstPluginsDir = Path.Combine(dstAssetsDir, flags.PluginsDir?.Length > 0 ? flags.PluginsDir : "Plugins");

            if (!Directory.Exists(dstAssetsDir))
            {
                throw new Exception("First argument must be the target project directory (should ");
            }

            if (!Directory.Exists(dstPluginsDir))
            {
                Directory.CreateDirectory(dstPluginsDir);
            }

            var csassemblies = Directory
                .EnumerateFiles(dstPluginsDir, "*" + Constants.AssemblyMapExt, SearchOption.AllDirectories)
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

                    if (fileID == Constants.LocalFileID && map.TryGetValue(guid, out var file))
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
    }
}
