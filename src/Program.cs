using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace SyncContextSample
{
    public static class Program
    {
        public static void Main()
        {
             using var contextsGroup = new SharedSynchronizationContext();
            
            // full load
            // var context = new SynchronizationContext();
            // load only 2 cores
            // using (var context = new MultiThreadSynchronizationContext(2, 2))
            // full load
            // using (var context = new MultiThreadSynchronizationContext(0, Environment.ProcessorCount))
            // CPU0 -> CPU1 -> CPU0 -> CPU1 -> CPU0 -> CPU1 ...
            // using (var context = new SwitchCoresSynchronizationContext(Environment.ProcessorCount))
             var context = contextsGroup.GetSynchronizationContext(1, 2, "context #1");
             var context2 = contextsGroup.GetSynchronizationContext(3, 2, "context #2");
            {
                var results = new ConcurrentQueue<string>();

                LoopLoop(context, results);
                Thread.Sleep(3000);
                 LoopLoop(context2, results);

                var spin = new SpinWait();
                var starvation = Stopwatch.StartNew();
                do
                {
                    while (results.TryDequeue(out var message))
                    { 
                        //Console.WriteLine(message);
                        starvation.Restart();
                    }
                    spin.SpinOnce();
                } while (starvation.ElapsedMilliseconds < 300);
            }
        }

        private static void LoopLoop(SynchronizationContext context, ConcurrentQueue<string> resultsMessages)
        {
            for (var i = 0; i < 500; ++i)
            {
                context.Post(index =>
                {
                    var howlong = Stopwatch.StartNew();
                    var res = 123;
                    var j=1;
                    while(howlong.ElapsedMilliseconds < 50)
                    {
                        unchecked {
                            res = res / j + j*j;
                            j++;
                        }
                    }
                    resultsMessages.Enqueue($"In thread {Thread.CurrentThread.ManagedThreadId}, index = {index}");
                }, i);
            }
        }
    }
}