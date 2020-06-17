using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SyncContextSample
{
    public partial class SharedSynchronizationContext
    {
        private protected class SharedSynchronizationContextImpl : SynchronizationContext, IDisposable
        {
            private readonly SharedSynchronizationContext _host;
            private readonly string _name;
            private readonly List<ThreadRunner> _runners;
            private readonly List<ThreadRunner> _importedThreads;
            private readonly List<IDisposable> _exportedRunners;
            private readonly ConcurrentQueue<LocalTask> _tasks;
            private readonly ManualResetEvent _hasJobEvent;
            
            private int _hardLock;

            public SharedSynchronizationContextImpl(
                SharedSynchronizationContext host,
                int startingCpu,
                int threadsCount,
                string name,
                CancellationToken token)
            {
                if (threadsCount < 0 || threadsCount > Environment.ProcessorCount)
                {
                    threadsCount = Environment.ProcessorCount;
                }
                
                _host = host;
                _name = name;
                _hasJobEvent = new ManualResetEvent(false);
                _runners = new List<ThreadRunner>();
                _importedThreads = new List<ThreadRunner>();
                _exportedRunners = new List<IDisposable>();
                _tasks = new ConcurrentQueue<LocalTask>();

                // Setup threads
                for (var processor = startingCpu; processor < startingCpu + threadsCount; processor++)
                {
                    _runners.Add(new ThreadRunner(
                        this, processor, _hasJobEvent, _tasks, token));
                }
            }

            internal bool HasWork => _tasks.Any();

            public string Name => _name;

            internal bool TryNotifyIdle(ThreadRunner threadRunner, out IDisposable registration)
            {
                Console.WriteLine($"- TryNotifyIdle on {_name}");
                lock (_exportedRunners)
                {
                    registration = null;
                    if (_exportedRunners.Count < _runners.Count)
                    {
                        registration = _host.TryExportIdleThread(threadRunner);
                        if (registration != null)
                        {
                            _exportedRunners.Add(registration);
                            return true;
                        }
                    }

                    Console.WriteLine($"- Failed to TryNotifyIdle: no work on {_name}. Blocking...");
                    
                    _hasJobEvent.Reset();
                    Interlocked.Exchange(ref _hardLock, 1);
                }

                return false;
            }

            internal void RegisterExternalRunner(ThreadRunner external)
            {
                lock (_importedThreads)
                {
                    _importedThreads.Add(external);
                }
                external.TasksSource = _tasks;
            }

            internal void RemoveExternalRunner(ThreadRunner external)
            {
                lock (_importedThreads)
                {
                    Console.WriteLine($"- RemoveExternalRunner from self: {_name}");
                    _importedThreads.Remove(external);
                    external.TasksSource = new ConcurrentQueue<LocalTask>();
                }
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                _tasks.Enqueue(new LocalTask {Action = d, State = state});

                if (Interlocked.Exchange(ref _hardLock, 0) == 1)
                {
                    _hasJobEvent.Set();
                    Console.WriteLine($"Waking up... {_name}");
                }
                else
                {
                    lock (_exportedRunners)
                    {
                        if (_exportedRunners.Any())
                        {
                            foreach (var exportedRegistration in _exportedRunners.ToList())
                            {
                                exportedRegistration.Dispose();
                                _exportedRunners.Remove(exportedRegistration);
                                _hasJobEvent.Set();
                            }
                        }
                    }
                }
            }

            public void Dispose()
            {
                // Remove self from parent
                _host.ContextDisposing(this);

                // Stop spinning in circles
                foreach (var runner in _runners)
                {
                    runner.Dispose();
                }

                _runners.Clear();
                _tasks.Clear();
            }
        }
    };
}