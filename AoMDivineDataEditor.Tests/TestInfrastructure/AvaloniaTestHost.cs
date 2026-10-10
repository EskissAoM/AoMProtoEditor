using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace AoMDivineDataEditor.Tests;

/// <summary>
/// Owns one continuously pumped Avalonia dispatcher for the whole test process.
/// Tests may run on different xUnit worker threads, but every control operation
/// is marshalled to the same UI thread.
/// </summary>
internal static class AvaloniaTestHost
{
    private static readonly Lazy<Host> Instance = new(() => new Host());

    public static void EnsureStarted() => _ = Instance.Value;

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Instance.Value.Dispatcher.InvokeAsync(action).GetAwaiter().GetResult();
    }

    public static T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Instance.Value.Dispatcher.InvokeAsync(action).GetAwaiter().GetResult();
    }

    public static T RunAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Instance.Value.Dispatcher.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task.GetAwaiter().GetResult();
    }

    public static void RunAsync(Func<Task> action)
        => RunAsync(async () =>
        {
            await action();
            return true;
        });

    /// <summary>
    /// Releases completed, unrooted UI graphs between tests. PowerEditorView is a large,
    /// eagerly-created control tree and a long sequence of workflow tests can otherwise
    /// grow the shared testhost working set faster than the runtime elects to compact it.
    /// </summary>
    public static void CollectUiGarbage()
    {
        if (!Instance.IsValueCreated)
            return;
        Run(() => Dispatcher.UIThread.RunJobs());
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed class Host
    {
        private readonly ManualResetEventSlim _ready = new(false);
        private Exception? _startupError;

        public Host()
        {
            var thread = new Thread(RunDispatcher)
            {
                IsBackground = true,
                Name = "AoM editor test UI"
            };
            thread.Start();
            _ready.Wait();
            if (_startupError != null)
                throw new InvalidOperationException("The Avalonia test dispatcher could not be started.", _startupError);
        }

        public Dispatcher Dispatcher => Dispatcher.UIThread;

        private void RunDispatcher()
        {
            try
            {
                AppBuilder.Configure<App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                AvaloniaSynchronizationContext.InstallIfNeeded();
                _ready.Set();
                Dispatcher.UIThread.MainLoop(CancellationToken.None);
            }
            catch (Exception exception)
            {
                _startupError = exception;
                _ready.Set();
            }
        }
    }
}
