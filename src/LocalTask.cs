using System.Threading;

namespace SyncContextSample
{
    internal struct LocalTask
    {
        public SendOrPostCallback Action;
        public object State;
    }
}