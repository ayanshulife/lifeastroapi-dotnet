using System;
using System.Threading;
using System.Threading.Tasks;

namespace LifeAstroApi.Pdf.Engine
{
    internal static class EngineTasks
    {
        /// <summary>A task that is not awaited on every path: its failure must not reach TaskScheduler.UnobservedTaskException.</summary>
        internal static void Observe(Task task)
        {
            _ = task.ContinueWith(
                finished => finished.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        internal static void CancelQuietly(CancellationTokenSource source)
        {
            try
            {
                source.Cancel();
            }
            catch (AggregateException)
            {
                // A callback failed; the source is cancelled all the same, and closing goes on.
            }
        }

        /// <summary>The task's result, unless the caller cancels first: the task itself runs on.</summary>
        internal static async Task<T> UnlessCancelledAsync<T>(Task<T> task, CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return await task.ConfigureAwait(false);
            }
        }

        /// <summary>Runs one phase under its timeout: a timeout is a LifeAstroPdfException, the caller's cancellation stays one.</summary>
        internal static async Task<T> WithTimeoutAsync<T>(
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string whatTimedOut,
            Func<CancellationToken, Task<T>> body)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linked.CancelAfter(timeout);
                try
                {
                    return await body(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException e)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(e.Message, e, cancellationToken);
                    }

                    if (linked.IsCancellationRequested)
                    {
                        throw new LifeAstroPdfException(whatTimedOut, "timeout", e);
                    }

                    throw;
                }
            }
        }
    }
}
