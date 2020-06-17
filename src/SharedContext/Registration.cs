using System;
using System.Collections.Concurrent;

namespace SyncContextSample
{
    public partial class SharedSynchronizationContext
    {
        private protected class Registration : IDisposable
        {
            private readonly ThreadRunner _sourceRunner;
            private readonly SharedSynchronizationContextImpl _targetContext;
            private readonly ConcurrentQueue<LocalTask> _nativeQueue;
            private bool _disposed = false;

            public Registration(ThreadRunner sourceRunner, SharedSynchronizationContextImpl targetContext)
            {
                Console.WriteLine($"- Registration on {targetContext.Name}");
                _sourceRunner = sourceRunner;
                _targetContext = targetContext;
                _nativeQueue = sourceRunner.TasksSource;
                _targetContext.RegisterExternalRunner(_sourceRunner);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _targetContext.RemoveExternalRunner(_sourceRunner);
                _sourceRunner.TasksSource = _nativeQueue;
            }
        }
    }
}