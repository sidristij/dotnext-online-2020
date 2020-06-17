using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace SyncContextSample
{
    internal class LocalThreadState
    {
        public Thread Thread;
        public CancellationTokenSource CancellationTokenSource;
        public ProcessThread ProcessThread;
        public int ProcessorId;
        public int ProcessorIdNext;

        public ConcurrentQueue<LocalTask> TasksSource { get; set; }
    }
}