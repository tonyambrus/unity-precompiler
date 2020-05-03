using CommandLine;
using System;

namespace UnityPrecompiler
{
    public class Flags
    {
        [Option('s', Required = true, HelpText = "Path to source project directory")]
        public string SrcPath { get; set; }

        [Option('d', Required = true, HelpText = "Path to destination project directory")]
        public string DstPath { get; set; }

        [Option('#', Required = false, Default = "", HelpText = "Optional preprocessor defines used to determine class info. Space separated, e.g: \"UNITY_EDITOR UNITY_WSA\") ")]
        public string Defines { get; set; }

        [Option('c', Required = false, Default = "Debug", HelpText = "Configuration to build assemblies (Debug/Release)")]
        public string Configuration { get; set; }

        [Option('f', Required = false, Default = "", HelpText = "Optional subdirectory of Assets to filter to")]
        public string FilterDir { get; set; }

        [Option('p', Required = false, Default = "Plugins", HelpText = "Plugin Directory relative to Assets directory in destination project directory")]
        public string PluginsDir { get; set; }

        [Option('k', Required = false, Default = false, HelpText = "Preserves target directories. Otherwise, files are mirrored from source project")]
        public bool KeepTargetFiles { get; set; }

        [Option('x', Required = false, HelpText = "Optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"")]
        public string Extensions { get; set; }

        [Option(Required = false, Default = true, HelpText = "Copies ProjectSettings folder to target")]
        public bool CopyProjectSettings { get; set; }

        [Option(Required = false, Default = true, HelpText = "Copies Packages directory (including manifest.json) to target")]
        public bool CopyPackages { get; set; }

        public static void Usage()
        {
            Console.WriteLine("Usage: UnityPrecompiler.exe compile -s srcPath -d dstPath [-Defines defines] [-c configuration] [-x extensions] [-p pluginDir] [-CopyProjectSettings bool] [-CopyPackages bool] [-FilterDir filterDir]");
            Console.WriteLine(" - srcPath: path to source project directory");
            Console.WriteLine(" - dstPath: path to target project directory");
            Console.WriteLine(" - defines: preprocessor defines used to determine class info");
            Console.WriteLine(" - configuration: Configuration to build assemblies (Debug/Release)");
            Console.WriteLine(" - pluginDir: Plugin Directory relative to Assets directory in destination project directory");
            Console.WriteLine(" - extensions: optional set of extension to only fix up. Space separated, e.g.: \"unity prefab mat asset cubemap ...\"");
            Console.WriteLine(" - filterDir: Optional subdirectory of Assets to filter to");
        }
    }
}
