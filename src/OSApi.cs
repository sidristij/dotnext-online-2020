using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SyncContextSample
{
    internal static class OSApi
    {
        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();

        public static void SetAffinityMaskForCurrentThread(int mask)
        {
            var currentThreadId = GetCurrentThreadId();
            var processThread = Process.GetCurrentProcess()
                .Threads.OfType<ProcessThread>()
                .First(pt => pt.Id == currentThreadId);
            processThread.ProcessorAffinity = (IntPtr) mask;
        }
    }
}