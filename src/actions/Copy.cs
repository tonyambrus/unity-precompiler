using System;
using System.Diagnostics;
using System.IO;

namespace UnityPrecompiler
{
    public static class Copy
    {
        public static void Execute(Flags flags)
        {
            var srcProjectDir = flags.SrcPath;
            var dstProjectDir = flags.DstPath;

            var subDir = flags.FilterDir?.Length > 0 ? Path.Combine("Assets", flags.FilterDir) : "Assets";
            var dstPath = Path.Combine(dstProjectDir, subDir);
            var srcPath = Path.Combine(srcProjectDir, subDir);
            if (!Directory.Exists(srcPath))
            {
                throw new Exception($"Can't find source directory {srcPath}");
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
            Console.WriteLine($"  - srcPath: {srcPath}");
            Console.WriteLine($"  - dstPath: {dstPath}");
            Console.WriteLine();
            Console.WriteLine($"Starting copy...");

            using (var wait = new WaitForProcesses())
            {
                wait.Add(MirrorAssets(srcPath, dstPath, flags.KeepTargetFiles));

                if (hasProjectSettingsDir)
                {
                    wait.Add(MirrorDirectory(srcProjectSettingsDir, dstProjectSettingsDir, flags.KeepTargetFiles));
                }

                if (hasPackagesDir)
                {
                    wait.Add(MirrorDirectory(srcPackagesDir, dstPackagesDir, flags.KeepTargetFiles));
                }
            }

            Console.WriteLine($"Copy Complete.");
        }


        private static string GetCopyParams(bool keep) => keep ? "/E" : "/MIR";
        private static Process MirrorAssets(string from, string to, bool keep) => ProcessUtil.StartHidden("robocopy", $"\"{from}\" \"{to}\" /XF *.cs *.cs.meta *.asmdef *.asmdef.meta {GetCopyParams(keep)}");
        private static Process MirrorDirectory(string from, string to, bool keep) => ProcessUtil.StartHidden("robocopy", $"\"{from}\" \"{to}\" {GetCopyParams(keep)}");
    }
}
