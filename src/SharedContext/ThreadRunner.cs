using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace SyncContextSample
{
    public partial class SharedSynchronizationContext
    {
        private protected class ThreadRunner
        {
            private readonly SharedSynchronizationContextImpl _host;
            private readonly CancellationTokenSource _cancellationTokenSource;
            private readonly int _processorId;
            private readonly ManualResetEvent _hasJobEvent;
            private IDisposable _exportRegistration;
            private readonly Thread _thread;

            public bool Exported => _exportRegistration != null;
            
            public ConcurrentQueue<LocalTask> TasksSource { get; set; }

            public ThreadRunner(
                SharedSynchronizationContextImpl host,
                int processorId,
                ManualResetEvent hasJobEvent,
                ConcurrentQueue<LocalTask> tasksSource,
                CancellationToken token)
            {
                _hasJobEvent = hasJobEvent;
                _host = host;
                _processorId = processorId;
                _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
                TasksSource = tasksSource;

                _thread = new Thread(Start);
                _thread.Start();
            }
            
            /// <summary>
            /// Thread method
            /// </summary>
            private void Start()
            {
                var curTid = OSApi.GetCurrentThreadId();
                
                var processThread = Process.GetCurrentProcess().Threads
                    .OfType<ProcessThread>()
                    .First(pt => pt.Id == curTid);

                processThread.IdealProcessor = _processorId;
                processThread.ProcessorAffinity = (IntPtr) (1 << _processorId);
                
                var token = _cancellationTokenSource.Token;
                var spinWait = new SpinWait();
                var starvation = Stopwatch.StartNew();
                
                while (!token.IsCancellationRequested)
                {
                    if (TasksSource.TryDequeue(out var task))
                    {
                        task.Action(task.State);
                        starvation.Restart();
                        continue;
                    }

                    if (starvation.ElapsedMilliseconds > 300)
                    {
                        // If I have no work and exported to another context,
                        // return me back to home context. Next iteration will loop 
                        // on home context's queue
                        if (Exported)
                        {
                            _exportRegistration.Dispose();
                            _exportRegistration = null;
                            continue;
                        }
                        
                        // If I have no work, I can ask for work in another context
                        // If another context in my host have any work, let's take 
                        // it's queue and join
                        if (_host.TryNotifyIdle(this, out var registration))
                        {
                            _exportRegistration = registration;
                        }
                        else
                        {
                            // else, go to blocking state
                            _hasJobEvent.Reset();
                            _hasJobEvent.WaitOne();
                            starvation.Restart();
                        }
                    }
                    
                    spinWait.SpinOnce();
                }
            }

            public void Dispose()
            {
                // throw cancellation event to all dependant contexts 
                _cancellationTokenSource?.Dispose();
                _thread.Join();
            }
        }
    }
}