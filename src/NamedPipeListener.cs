using System;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Threading;

namespace Jvs.MultiPipes
{
  public class NamedPipeListener : IDisposable
  {
    private ManualResetEventSlim connection_received_ = new();
    private string server_name_ = null;
    public string Name
    {
      get => server_name_;
      set
      {
        ThrowIfListening("set the server name");
        server_name_ = value;
      }
    }

    public WaitHandle ConnectionReceivedEvent => connection_received_.WaitHandle;

    public bool IsListening
    {
      get;
      private set;
    }

    private NamedPipeServerStream Server { get; set; } = null;

    private ConcurrentQueue<NamedPipeServerStream> Clients { get; } = new();

    public NamedPipeListener(string name)
    {
      server_name_ = name;
    }

    public async Task<NamedPipeClient> AcceptAsync(
      int timeoutMs, CancellationToken cancellationToken)
    {
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
          : Math.Min(MultiPipes.CancellationCheckIntervalMs, waitTime));
        var acceptResult = await DequeueClientAsync(waitTime, cancellationToken);
        if (!acceptResult.Ready)
        {
          // Timeout was elapsed, even if just for the local wait time. If it wasn't the local wait
          // time, the loop condition will handle it properly.
          spinWait.SpinOnce();
          continue;
        }

        if (acceptResult.Stream == null)
        {
          // Timeout didn't elapse, but the dequeue failed. Keep waiting and trying.
          spinWait.SpinOnce();
          continue;
        }

        return new NamedPipeClient(server_name_, new NamedPipeStream(acceptResult.Stream));
      } while (isInfinite || (elapsedTime = Environment.TickCount - startTime) < timeoutMs);
      throw new TimeoutException();
    }

    public async Task<NamedPipeClient> AcceptAsync(
      TimeSpan timeout, CancellationToken cancellationToken) 
      => await AcceptAsync((int)timeout.TotalMilliseconds, cancellationToken);

    public async Task<NamedPipeClient> AcceptAsync(int timeoutMs)
      => await AcceptAsync(timeoutMs, CancellationToken.None);

    public async Task<NamedPipeClient> AcceptAsync(TimeSpan timeout)
      => await AcceptAsync((int)timeout.TotalMilliseconds, CancellationToken.None);

    public async Task<NamedPipeClient> AcceptAsync(CancellationToken cancellationToken)
      => await AcceptAsync(Timeout.Infinite, cancellationToken);

    public async Task<NamedPipeClient> AcceptAsync()
      => await AcceptAsync(Timeout.Infinite, CancellationToken.None);

    public NamedPipeClient Accept(int timeoutMs)
    {
      int startTime = Environment.TickCount;
      bool isInfinite = (timeoutMs == Timeout.Infinite);
      int ellapsedTime = 0;
      do
      {
        int waitTime = timeoutMs - ellapsedTime;
        var acceptResult = DequeueClient(timeoutMs, CancellationToken.None);
        if (!acceptResult.Ready)
        {
          // Timeout was elapsed, even if just for the local wait time. If it wasn't the local wait
          // time, the loop condition will handle it properly.
          continue;
        }

        if (acceptResult.Stream == null)
        {
          // Timeout didn't elapse, but the dequeue failed. Keep waiting and trying.
          continue;
        }

        return new NamedPipeClient(server_name_, new NamedPipeStream(acceptResult.Stream));
      } while (isInfinite || (ellapsedTime = Environment.TickCount - startTime) < timeoutMs);
      throw new TimeoutException();
    }

    public NamedPipeClient Accept(TimeSpan timeout)
      => Accept((int)timeout.TotalMilliseconds);

    public NamedPipeClient Accept()
      => Accept(Timeout.Infinite);

    public void Start()
    {
      ThrowIfListening();
      DoListen();
    }

    public void Stop()
    {
      ThrowIfListening(false);
      StopInternal();
    }

    public void Dispose()
    {
      StopInternal();
      connection_received_.Dispose();
    }

    private async Task<(NamedPipeServerStream Stream, bool Ready)> DequeueClientAsync(
      int waitTime, CancellationToken cancellationToken)
    {
      return await Task.Run(() => DequeueClient(waitTime, cancellationToken));
    }

    private (NamedPipeServerStream Stream, bool Ready) DequeueClient(
      int waitTime, CancellationToken cancellationToken)
    {
      NamedPipeServerStream serverStream = null;
      bool clientReady = connection_received_.Wait(waitTime, cancellationToken);
      if (!clientReady)
      {
        return (serverStream, clientReady);
      }

      if (!Clients.TryDequeue(out serverStream))
      {
        if (Clients.IsEmpty)
        {
          // If the server was stopped or disposed, this will throw an ObjectDisposedException. We
          // don't want to catch this and allow it to bubble up.
          connection_received_.Reset();
        }
      }

      return (serverStream, clientReady);
    }

    private void StopInternal()
    {
      connection_received_.Reset();
      Server?.Dispose();
      Server = null;
      foreach (var serverStream in Clients)
      {
        serverStream.Dispose();
      }

      Clients.Clear();
      IsListening = false;
    }

    private void DoListen()
    {
      Server = new NamedPipeServerStream(
        pipeName: Name,
        direction: PipeDirection.InOut,
        maxNumberOfServerInstances: MultiPipes.DefaultBacklog,
        transmissionMode: PipeTransmissionMode.Byte,
        options: PipeOptions.Asynchronous);
      Server.BeginWaitForConnection(OnConnection, state: this);
    }

    private static void OnConnection(IAsyncResult ar)
    {
      var @this = ar.AsyncState as NamedPipeListener;
      try
      {
        // @this.Server could be null if it was disposed.
        if (@this.Server != null)
        {
          @this.Server.EndWaitForConnection(ar);
          @this.Clients.Enqueue(@this.Server);
          @this.connection_received_.Set();
          // Restart the listener.
          @this.DoListen();
        }
      }
      catch (ObjectDisposedException)
      {
        // Server was stopped or disposed before a connection could be completed.
        @this.Server?.Dispose();
        @this.Server = null;
        @this.IsListening = false;
      }
    }

    private void ThrowIfListening(bool isListening, string operation)
    {
      string state = isListening ? "listening for clients" : "not listening for clients";
      if (IsListening == isListening)
      {
        if (string.IsNullOrEmpty(operation))
        {
          throw new InvalidOperationException($"Cannot perform this operation while {state}.");
        }
        else
        {
          throw new InvalidOperationException($"Cannot {operation} while {state}.");
        }
      }
    }

    private void ThrowIfListening(bool isListening) => 
      ThrowIfListening(isListening, operation: null);

    private void ThrowIfListening(string operation) => 
      ThrowIfListening(isListening: true, operation);

    private void ThrowIfListening() =>
      ThrowIfListening(isListening: true, operation: null);
  }
}
