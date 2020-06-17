﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Diagnostics;

namespace SyncContextSample
{
    public class MultiThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private volatile int _hardLock;
        private readonly ConcurrentBag<LocalThreadState> _threads;
        private readonly ConcurrentQueue<LocalTask> _tasks;
        private readonly AutoResetEvent _event;

        public MultiThreadSynchronizationContext(int startingCpu, int cpuCount = int.MinValue, CancellationTokenSource cts = default)
        {
            if (cpuCount < 0) cpuCount = Environment.ProcessorCount;

            _threads = new ConcurrentBag<LocalThreadState>();
            _tasks = new ConcurrentQueue<LocalTask>();
            _event = new AutoResetEvent(false);

            // Setup threads
            for(var processor = startingCpu; processor < startingCpu + cpuCount; processor++)
            {
                var thread = new Thread(Start)
                {
                    Name = $"{nameof(MultiThreadSynchronizationContext)} #{processor}"
                };
                
                var lts = new LocalThreadState
                {
                    Thread = thread,
                    ProcessorId = processor,
                    CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cts?.Token ?? default)
                };
                thread.Start(lts);
                _threads.Add(lts);
            }
        }

        /// <summary>
        /// SyncContext thread function
        /// </summary>
        private void Start(object obj)
        {
            SetSynchronizationContext(this);
            var tinfo = (LocalThreadState)obj;
            var curTid = OSApi.GetCurrentThreadId();
            
            var processThread = Process.GetCurrentProcess().Threads.OfType<ProcessThread>().First(pt => pt.Id == curTid);
            processThread.ProcessorAffinity = (IntPtr) (1 << tinfo.ProcessorId);
            processThread.IdealProcessor = tinfo.ProcessorId;
            tinfo.ProcessThread = processThread;

            var token = tinfo.CancellationTokenSource.Token;
            var spinWait = new SpinWait();
            var starvation = Stopwatch.StartNew();
            
            while (!token.IsCancellationRequested)
            {
                if (_tasks.TryDequeue(out var task))
                {
                    task.Action(task.State);
                    starvation.Restart();
                }
                else
                {
                    if (starvation.ElapsedMilliseconds > 300)
                    {
                        Interlocked.Exchange(ref _hardLock, 1);
                        _event.WaitOne();
                        starvation.Restart();
                        continue;
                    }
                    spinWait.SpinOnce();
                }
            }
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            throw new NotSupportedException("Please, use Post() instead");
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            _tasks.Enqueue(new LocalTask{Action = d, State = state});
            if (Interlocked.Exchange(ref _hardLock, 0) == 1)
            {
                _event.Set();
            }
        }

        public void Dispose()
        {
            // Stop spinning in circles
            foreach (var tokenSource in _threads.Select(x => x.CancellationTokenSource))
            {
                tokenSource.Cancel();
            }

            // Unlock threads
            Interlocked.Exchange(ref _hardLock, 0);
            _event.Set();

            // Wait for completion
            foreach (var threadState in _threads)
            {
                threadState.Thread.Join();
            }

            _threads.Clear();
            _tasks.Clear();
            _event.Dispose();
        }
    }
}