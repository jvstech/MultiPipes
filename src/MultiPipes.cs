using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jvs.MultiPipes
{
  internal class MultiPipes
  {
    public const int DefaultBacklog = NamedPipeServerStream.MaxAllowedServerInstances;
    public const int CancellationCheckIntervalMs = 50;
    public const int DefaultBufferSize = 0x2000; // Two pages of data

    internal static void WaitUntil(
      Func<bool> waitFunc, int timeoutMs, CancellationToken cancellationToken)
    {
      if (waitFunc == null)
      {
        return;
      }

      WaitUntilAsync(waitFunc, timeoutMs, cancellationToken).RunSynchronously();
    }

    internal static async Task WaitUntilAsync(
      Func<bool> waitFunc, int timeoutMs, CancellationToken cancellationToken)
    {
      if (waitFunc == null)
      {
        return;
      }

      int startTime = Environment.TickCount;
      int elapsedTime = 0;
      bool isInfinite = (timeoutMs == Timeout.Infinite);

      do
      {
        cancellationToken.ThrowIfCancellationRequested();
        int waitTime = timeoutMs - elapsedTime;
        waitTime = (!isInfinite
          ? Math.Min(CancellationCheckIntervalMs, waitTime)
          : CancellationCheckIntervalMs);

        if (waitFunc())
        {
          return;
        }

        await Task.Delay(waitTime, cancellationToken);
      } while (isInfinite || (elapsedTime = Environment.TickCount - startTime) < timeoutMs);
      throw new TimeoutException();
    }

    internal static async Task WaitUntilAsync(
      WaitHandle waitHandle, int timeoutMs, CancellationToken cancellationToken)
    {
      if (waitHandle == null)
      {
        throw new ArgumentNullException(nameof(waitHandle));
      }

      int startTime = Environment.TickCount;
      int elapsedTime = 0;
      bool isInfinite = (timeoutMs == Timeout.Infinite);

      SpinWait spinWait = new();
      do
      {
        cancellationToken.ThrowIfCancellationRequested();
        int waitTime = timeoutMs - elapsedTime;
        waitTime = (cancellationToken.CanBeCanceled 
          ? waitTime 
          : Math.Min(CancellationCheckIntervalMs, waitTime));

        if (await WaitOneAsync(waitHandle, waitTime, cancellationToken))
        {
          return;
        }

        
        spinWait.SpinOnce();
      } while (isInfinite || (elapsedTime = Environment.TickCount - startTime) < timeoutMs);
      throw new TimeoutException();
    }

    internal static Task WaitUntilAsync(
      ManualResetEventSlim waitHandle, int timeoutMs, CancellationToken cancellationToken)
    {
      if (waitHandle == null)
      {
        throw new ArgumentNullException(nameof(waitHandle));
      }

      return Task.Factory.StartNew(() =>
      {
        int startTime = Environment.TickCount;
        int elapsedTime = 0;
        bool isInfinite = (timeoutMs == Timeout.Infinite);
        do
        {
          cancellationToken.ThrowIfCancellationRequested();
          int waitTime = timeoutMs - elapsedTime;
          waitTime = (cancellationToken.CanBeCanceled
            ? waitTime
            : Math.Min(CancellationCheckIntervalMs, waitTime));
          if (waitHandle.Wait(waitTime, cancellationToken))
          {
            return;
          }
        } while (isInfinite || (elapsedTime = Environment.TickCount - startTime) < timeoutMs);
        throw new TimeoutException();
      });
    }

    internal static async Task<bool> WaitOneAsync(
      WaitHandle waitHandle, int timeoutMs, CancellationToken cancellationToken)
    {
      RegisteredWaitHandle registeredHandle = null;
      CancellationTokenRegistration tokenRegistration = default;
      try
      {
        var tcs = new TaskCompletionSource<bool>();
        registeredHandle = ThreadPool.RegisterWaitForSingleObject(waitHandle,
          (state, timedOut) => ((TaskCompletionSource<bool>)state).TrySetResult(!timedOut),
          state: tcs,
          timeoutMs,
          executeOnlyOnce: true);
        tokenRegistration = cancellationToken.Register(
          state => ((TaskCompletionSource<bool>)state).TrySetCanceled(),
          tcs);
        return await tcs.Task;
      }
      finally
      {
        if (registeredHandle != null)
        {
          registeredHandle.Unregister(null);
        }

        tokenRegistration.Dispose();
      }
    }

    internal static async Task<int> WaitAnyAsync(
      int timeoutMs, CancellationToken cancellationToken, IEnumerable<ManualResetEventSlim> waitHandles)
    {
      if (waitHandles == null)
      {
        throw new ArgumentNullException(nameof(waitHandles));
      }

      if (!waitHandles.Any() || waitHandles.Any(h => h == null))
      {
        throw new ArgumentException(
          "One or more event handles were null or no event handles were provided", 
          nameof(waitHandles));
      }

      var waitTasks = waitHandles
        .Select(h => WaitUntilAsync(h, Timeout.Infinite, cancellationToken))
        .ToList();
      var timeoutExpiredTask = WaitUntilAsync(() => false, timeoutMs, cancellationToken);
      waitTasks.Insert(0, timeoutExpiredTask);

      var doneTask = await Task.WhenAny(waitTasks);
      if (doneTask.IsFaulted)
      {
        throw doneTask.Exception.InnerException;
      }

      return waitTasks.IndexOf(doneTask) - 1;
    }

    internal static int WaitAny(int timeoutMs, CancellationToken cancellationToken, 
      IEnumerable<ManualResetEventSlim> waitHandles)
    {
      var task = WaitAnyAsync(timeoutMs, cancellationToken, waitHandles);
      return task.Result;
    }
  }
}
