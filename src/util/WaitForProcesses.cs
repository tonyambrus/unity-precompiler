using System;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace UnityPrecompiler
{
    public class WaitForProcesses : IDisposable
    {
        private ConcurrentQueue<Process> queue = new ConcurrentQueue<Process>();

        public void Add(Process process)
        {
            if (process != null)
            {
                queue.Enqueue(process);
            }
        }

        public void Dispose()
        {
            while (queue.Count > 0)
            {
                if (queue.TryDequeue(out var p))
                {
                    p.WaitForExit();
                }
            }
        }
    }
}
