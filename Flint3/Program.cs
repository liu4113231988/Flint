using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace Flint3
{
    public static class Program
    {
        private const string InstanceKey = "NoMewing.sh0ckj0ckey.Flint3";

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                using WindowsIdentity user = WindowsIdentity.GetCurrent();
                // 免打包副本从不同目录启动时也共享当前用户、当前会话的实例锁。
                string name = $@"Local\{InstanceKey}.{user.User!.Value}";
                using EventWaitHandle activation = new(false, EventResetMode.AutoReset, name + ".Activate");
                using Mutex instance = new(false, name);
                if (!TryOwnInstance(instance))
                {
                    activation.Set();
                    return;
                }

                RegisteredWaitHandle? listener = null;
                try
                {
                    WinRT.ComWrappersSupport.InitializeComWrappers();
                    Microsoft.UI.Xaml.Application.Start(parameters =>
                    {
                        DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
                        SynchronizationContext.SetSynchronizationContext(context);
                        App app = new();
                        listener = ThreadPool.RegisterWaitForSingleObject(
                            activation, (_, _) => app.ActivateExistingWindow(), null, Timeout.Infinite, executeOnlyOnce: false);
                    });
                }
                finally
                {
                    listener?.Unregister(null);
                    instance.ReleaseMutex();
                }
            }
            catch (Exception e)
            {
                Environment.ExitCode = 1;
                Trace.WriteLine(e);
            }
        }

        private static bool TryOwnInstance(Mutex instance)
        {
            try
            {
                return instance.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
    }
}
