﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SyncContextSample
{
    public partial class SharedSynchronizationContext : IDisposable
    {
        private List<SharedSynchronizationContextImpl> _bag;
        private CancellationTokenSource _cts;
        
        public SharedSynchronizationContext()
        {
            _cts = new CancellationTokenSource();
            _bag = new List<SharedSynchronizationContextImpl>();         
        }

        public SynchronizationContext GetSynchronizationContext(int startingCpu, int threadsCount, string name = null, CancellationToken token = default)
        {
            var context = new SharedSynchronizationContextImpl(
                this, startingCpu, threadsCount, name,
                CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, token).Token);

            lock (_bag)
            {
                _bag.Add(context);
            }
            
            return context;
        }

        private IDisposable TryExportIdleThread(ThreadRunner runner)
        {
            lock (_bag)
            {
                foreach (var impl in _bag.Where(impl => impl.HasWork))
                {
                    return new Registration(runner, impl);
                }
            }

            return null;
        }

        private void ContextDisposing(SharedSynchronizationContextImpl context)
        {
            lock (_bag)
            {
                _bag.Remove(context);
            }
        }
        
        public void Dispose()
        {
            _cts.Cancel();
            lock (_bag)
            {
                foreach (var contextImpl in _bag.ToList())
                {
                    contextImpl.Dispose();
                }
            }
        }
    }
}