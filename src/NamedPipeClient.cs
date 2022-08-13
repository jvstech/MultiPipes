using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jvs.MultiPipes
{
  public class NamedPipeClient : IDisposable
  {
    private NamedPipeStream stream_ = null;
    
    public bool IsConnected => stream_?.IsConnected ?? false;

    public NamedPipeClient()
    {
    }

    public NamedPipeClient(string name)
    {
      Name = name;
    }

    internal NamedPipeClient(string name, NamedPipeStream serverStream)
    {
      stream_ = serverStream;
    }

    private string name_ = null;
    public string Name
    {
      get => name_;
      set
      {
        ThrowIfConnected("set the server name");
        name_ = value;
      }
    }

    #region ConnectAsync

    // 1111

    public async Task ConnectAsync(string name, int timeoutMs, CancellationToken cancellationToken, 
      TokenImpersonationLevel impersonationLevel)
    {
      ThrowIfConnected();
      CreateClientStream(name, impersonationLevel);
      await stream_.ConnectAsync(timeoutMs, cancellationToken);
    }

    // 1111

    public async Task ConnectAsync(string name, TimeSpan timeout, 
      CancellationToken cancellationToken, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(
        name, (int)timeout.TotalMilliseconds, cancellationToken, impersonationLevel);

    // 1110

    public async Task ConnectAsync(string name, int timeoutMs, CancellationToken cancellationToken)
      => await ConnectAsync(name, timeoutMs, cancellationToken, TokenImpersonationLevel.None);

    // 1110

    public async Task ConnectAsync(
      string name, TimeSpan timeout, CancellationToken cancellationToken)
      => await ConnectAsync(
        name, (int)timeout.TotalMilliseconds, cancellationToken, TokenImpersonationLevel.None);

    // 1101

    public async Task ConnectAsync(
      string name, int timeoutMs, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(name, timeoutMs, CancellationToken.None, impersonationLevel);

    // 1101

    public async Task ConnectAsync(
      string name, TimeSpan timeout, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(
        name, (int)timeout.TotalMilliseconds, CancellationToken.None, impersonationLevel);

    // 1100

    public async Task ConnectAsync(string name, int timeoutMs)
      => await ConnectAsync(name, timeoutMs, CancellationToken.None, TokenImpersonationLevel.None);

    // 1100
    public async Task ConnectAsync(string name, TimeSpan timeout)
      => await ConnectAsync(
        name, (int)timeout.TotalMilliseconds, CancellationToken.None, TokenImpersonationLevel.None);

    // 1011

    public async Task ConnectAsync(
      string name, CancellationToken cancellationToken, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(name, Timeout.Infinite, cancellationToken, impersonationLevel);

    // 1010

    public async Task ConnectAsync(string name, CancellationToken cancellationToken)
      => await ConnectAsync(
        name, Timeout.Infinite, cancellationToken, TokenImpersonationLevel.None);

    // 1001

    public async Task ConnectAsync(string name, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(name, Timeout.Infinite, CancellationToken.None, impersonationLevel);

    // 1000

    public async Task ConnectAsync(string name)
      => await ConnectAsync(
        name, Timeout.Infinite, CancellationToken.None, TokenImpersonationLevel.None);

    // 0111

    public async Task ConnectAsync(int timeoutMs, CancellationToken cancellationToken, 
      TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(name: null, timeoutMs, cancellationToken, impersonationLevel);

    // 0111

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken, 
      TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(
        name: null, (int)timeout.TotalMilliseconds, cancellationToken, impersonationLevel);

    // 0110

    public async Task ConnectAsync(int timeoutMs, CancellationToken cancellationToken)
      => await ConnectAsync(name: null, timeoutMs, cancellationToken, TokenImpersonationLevel.None);

    // 0110

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
      => await ConnectAsync(name: null, (int)timeout.TotalMilliseconds, cancellationToken, 
        TokenImpersonationLevel.None);

    // 0101

    public async Task ConnectAsync(int timeoutMs, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(name: null, timeoutMs, CancellationToken.None, impersonationLevel);

    // 0101

    public async Task ConnectAsync(TimeSpan timeout, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(
        name: null, (int)timeout.TotalMilliseconds, CancellationToken.None, impersonationLevel);

    // 0100

    public async Task ConnectAsync(int timeoutMs)
      => await ConnectAsync(
        name: null, timeoutMs, CancellationToken.None, TokenImpersonationLevel.None);

    // 0100

    public async Task ConnectAsync(TimeSpan timeout)
      => await ConnectAsync(name: null, (int)timeout.TotalMilliseconds, CancellationToken.None,
        TokenImpersonationLevel.None);

    // 0011

    public async Task ConnectAsync(
      CancellationToken cancellationToken, TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(name: null, Timeout.Infinite, cancellationToken, impersonationLevel);

    // 0010

    public async Task ConnectAsync(CancellationToken cancellationToken)
      => await ConnectAsync(
        name: null, Timeout.Infinite, cancellationToken, TokenImpersonationLevel.None);

    // 0001

    public async Task ConnectAsync(TokenImpersonationLevel impersonationLevel)
      => await ConnectAsync(
        name: null, Timeout.Infinite, CancellationToken.None, impersonationLevel);

    // 0000

    public async Task ConnectAsync()
      => await ConnectAsync(
        name: null, Timeout.Infinite, CancellationToken.None, TokenImpersonationLevel.None);

    #endregion

    #region Connect

    // 111
    
    public void Connect(string name, int timeoutMs, TokenImpersonationLevel impersonationLevel)
    {
      ThrowIfConnected();
      CreateClientStream(name, impersonationLevel);
      stream_.Connect(timeoutMs);
    }

    // 111

    public void Connect(string name, TimeSpan timeout, TokenImpersonationLevel impersonationLevel)
      => Connect(name, (int)timeout.TotalMilliseconds, impersonationLevel);

    // 110

    public void Connect(string name, int timeoutMs)
      => Connect(name, timeoutMs);

    // 110

    public void Connect(string name, TimeSpan timeout)
      => Connect(name, (int)timeout.TotalMilliseconds);

    // 101

    public void Connect(string name, TokenImpersonationLevel impersonationLevel)
      => Connect(name, Timeout.Infinite, impersonationLevel);

    // 100

    public void Connect(string name)
      => Connect(name, Timeout.Infinite, TokenImpersonationLevel.None);

    // 011

    public void Connect(int timeoutMs, TokenImpersonationLevel impersonationLevel)
      => Connect(name: null, timeoutMs, impersonationLevel);

    // 011

    public void Connect(TimeSpan timeout, TokenImpersonationLevel impersonationLevel)
      => Connect(name: null, (int)timeout.TotalMilliseconds, impersonationLevel);

    // 010

    public void Connect(int timeoutMs)
      => Connect(name: null, timeoutMs, TokenImpersonationLevel.None);

    // 010

    public void Connect(TimeSpan timeout)
      => Connect(name: null, (int)timeout.TotalMilliseconds, TokenImpersonationLevel.None);

    // 001

    public void Connect(TokenImpersonationLevel impersonationLevel)
      => Connect(name: null, Timeout.Infinite, impersonationLevel);

    // 000

    public void Connect()
      => Connect(name: null, Timeout.Infinite, TokenImpersonationLevel.None);

    #endregion

    public void Disconnect() => Close();

    public void Close()
    {
      ThrowIfConnected(false);
      CloseInternal();
    }

    public void Dispose()
    {
      CloseInternal();
    }

    public NamedPipeStream GetStream()
    {
      ThrowIfConnected(false);
      return stream_;
    }

    #region Private methods

    private void CloseInternal()
    {
      Debug.WriteLine("NamedPipeClient.CloseInternal() called");
      stream_?.Dispose();
      stream_ = null;
    }

    private void CreateClientStream(string name, TokenImpersonationLevel impersonationLevel)
    {
      if (!string.IsNullOrEmpty(name))
      {
        Name = name;
      }

      stream_ = new NamedPipeStream(new NamedPipeClientStream(
        serverName: ".",
        pipeName: Name,
        direction: PipeDirection.InOut,
        options: PipeOptions.Asynchronous,
        impersonationLevel));
    }

    private void ThrowIfConnected(bool isConnected, string operation)
    {
      string state = isConnected ? "connected" : "disconnected";
      if (IsConnected == isConnected)
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

    private void ThrowIfConnected(string operation) => 
      ThrowIfConnected(isConnected: true, operation);

    private void ThrowIfConnected(bool isConnected) => 
      ThrowIfConnected(isConnected, operation: null);

    private void ThrowIfConnected() => 
      ThrowIfConnected(isConnected: true, operation: null);

    #endregion
  }
}
