using System.Collections.Generic;
using System.Diagnostics;

namespace UnityPrecompiler
{
    public static class ProcessUtil
    {
        public static Process StartHidden(string filename, string args)
        {
            var process = new Process();
            var startinfo = new ProcessStartInfo(filename, args)
            {
                WindowStyle = ProcessWindowStyle.Hidden
            };
            process.StartInfo = startinfo;
            process.Start();
            return process;
        }

        public static List<string> ExecuteReadOutput(string filename, string args)
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

        public static List<string> ExecuteReadError(string filename, string args, out int exitCode)
        {
            var list = new List<string>();
            var process = new Process();
            var startinfo = new ProcessStartInfo(filename, args)
            {
                RedirectStandardError = true,
                UseShellExecute = false
            };
            process.StartInfo = startinfo;
            process.ErrorDataReceived += (_, a) => list.Add(a.Data);
            process.Start();
            process.BeginErrorReadLine();
            process.WaitForExit();
            exitCode = process.ExitCode;
            return list;
        }
    }
}
