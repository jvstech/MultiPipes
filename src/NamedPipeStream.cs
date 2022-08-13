using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jvs.MultiPipes
{
  public class NamedPipeStream : Stream
  {
    private readonly NamedPipeClientStream client_;
    private readonly NamedPipeServerStream server_;
    private readonly PipeStream stream_;
    private readonly ReadBufferStream read_stream_ = new ReadBufferStream();
    private readonly ManualResetEventSlim on_connected_ = new(false);
    private readonly ManualResetEventSlim on_disconnected_ = new(true);
    private readonly ManualResetEventSlim on_data_available_ = new(false);
    private readonly Thread read_thread_ = new Thread(DoRead);
    private bool disposed_;
    private bool is_connected_;

    internal NamedPipeStream(NamedPipeClientStream client)
    {
      client_ = client;
      stream_ = client_;
      read_thread_.Start(this);
      IsConnected = client_.IsConnected;
    }

    public NamedPipeStream(NamedPipeServerStream server)
    {
      server_ = server;
      stream_ = server_;
      read_thread_.Start(this);
      IsConnected = server_.IsConnected;
    }

    public bool IsConnected
    {
      get
      {
        if (stream_.IsConnected != is_connected_)
        {
          is_connected_ = stream_.IsConnected;
          if (!is_connected_)
          {
            on_connected_.Reset();
            on_disconnected_.Set();
          }
          else
          {
            on_connected_.Set();
            on_disconnected_.Reset();
          }
        }

        return is_connected_;
      }

      internal set
      {
        is_connected_ = (value && stream_.IsConnected);
        if (!is_connected_)
        {
          on_connected_.Reset();
          on_disconnected_.Set();
        }
        else
        {
          on_connected_.Set();
          on_disconnected_.Reset();
        }
      }
    }

    public bool DataAvailable 
      => on_data_available_.IsSet || (read_stream_?.OnDataAvailable.IsSet ?? false);

    public bool WaitForData(int timeoutMs, CancellationToken cancellationToken)
    {
      return on_data_available_.Wait(timeoutMs, cancellationToken);
    }

    public bool WaitForData(TimeSpan timeout, CancellationToken cancellationToken)
      => WaitForData((int)timeout.TotalMilliseconds, cancellationToken);

    public bool WaitForData(CancellationToken cancellationToken)
      => WaitForData(Timeout.Infinite, cancellationToken);

    public bool WaitForData(int timeoutMs)
      => WaitForData(timeoutMs, CancellationToken.None);

    public bool WaitForData(TimeSpan timeout)
      => WaitForData((int)timeout.TotalMilliseconds, CancellationToken.None);

    public bool WaitForData()
      => WaitForData(Timeout.Infinite, CancellationToken.None);

    public async Task<bool> WaitForDataAsync(int timeoutMs, CancellationToken cancellationToken)
    {
      try
      {
        await MultiPipes.WaitUntilAsync(on_data_available_, timeoutMs, cancellationToken);
        return on_data_available_.IsSet;
      }
      catch (Exception ex) when
      (
        (ex is TimeoutException) ||
        (ex is AggregateException ae && ae.InnerException is TimeoutException)
      )
      {
        return false;
      }
    }

    public async Task<bool> WaitForDataAsync(TimeSpan timeout, CancellationToken cancellationToken)
      => await WaitForDataAsync((int)timeout.TotalMilliseconds, cancellationToken);

    public async Task<bool> WaitForDataAsync(CancellationToken cancellationToken)
      => await WaitForDataAsync(Timeout.Infinite, cancellationToken);

    public async Task<bool> WaitForDataAsync(int timeoutMs)
      => await WaitForDataAsync(timeoutMs, CancellationToken.None);

    public async Task<bool> WaitForDataAsync(TimeSpan timeout)
      => await WaitForDataAsync((int)timeout.TotalMilliseconds, CancellationToken.None);

    public async Task<bool> WaitForDataAsync()
      => await WaitForDataAsync(Timeout.Infinite, CancellationToken.None);

    public bool WaitForDisconnect(int timeoutMs, CancellationToken cancellationToken)
    {
      return on_disconnected_.Wait(timeoutMs, cancellationToken);
    }

    public bool WaitForDisconnect(TimeSpan timeout, CancellationToken cancellationToken)
      => WaitForDisconnect((int)timeout.TotalMilliseconds, cancellationToken);

    public bool WaitForDisconnect(CancellationToken cancellationToken)
      => WaitForDisconnect(Timeout.Infinite, cancellationToken);

    public bool WaitForDisconnect(int timeoutMs)
      => WaitForDisconnect(timeoutMs, CancellationToken.None);

    public bool WaitForDisconnect(TimeSpan timeout)
      => WaitForDisconnect((int)timeout.TotalMilliseconds, CancellationToken.None);

    public bool WaitForDisconnect()
      => WaitForDisconnect(Timeout.Infinite, CancellationToken.None);

    public async Task<bool> WaitForDisconnectAsync(int timeoutMs, CancellationToken cancellationToken)
    {
      try
      {
        await MultiPipes.WaitUntilAsync(on_disconnected_, timeoutMs, cancellationToken);
        return on_data_available_.IsSet;
      }
      catch (Exception ex) when
      (
        (ex is TimeoutException) ||
        (ex is AggregateException ae && ae.InnerException is TimeoutException)
      )
      {
        return false;
      }
    }

    public async Task<bool> WaitForDisconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
      => await WaitForDisconnectAsync((int)timeout.TotalMilliseconds, cancellationToken);

    public async Task<bool> WaitForDisconnectAsync(CancellationToken cancellationToken)
      => await WaitForDisconnectAsync(Timeout.Infinite, cancellationToken);

    public async Task<bool> WaitForDisconnectAsync(int timeoutMs)
      => await WaitForDisconnectAsync(timeoutMs, CancellationToken.None);

    public async Task<bool> WaitForDisconnectAsync(TimeSpan timeout)
      => await WaitForDisconnectAsync((int)timeout.TotalMilliseconds, CancellationToken.None);

    public async Task<bool> WaitForDisconnectAsync()
      => await WaitForDisconnectAsync(Timeout.Infinite, CancellationToken.None);

    public (bool Data, bool Disconnect) WaitForDataOrDisconnect(
      int timeoutMs, CancellationToken cancellationToken)
    {
      try
      {
        int idx = MultiPipes.WaitAny(
          timeoutMs, cancellationToken, new[] { on_data_available_, on_disconnected_ });
        return (Data: idx == 0, Disconnect: idx == 1);
      }
      catch (Exception ex) when
      (
        (ex is TimeoutException) ||
        (ex is AggregateException ae && ae.InnerException is TimeoutException)
      )
      {
        return (Data: false, Disconnect: false);
      }
    }

    public (bool Data, bool Disconnect) WaitForDataOrDisconnect(
      TimeSpan timeout, CancellationToken cancellationToken)
      => WaitForDataOrDisconnect((int)timeout.TotalMilliseconds, cancellationToken);

    public (bool Data, bool Disconnect) WaitForDataOrDisconnect(int timeoutMs)
      => WaitForDataOrDisconnect(timeoutMs, CancellationToken.None);

    public (bool Data, bool Disconnect) WaitForDataOrDisconnect(TimeSpan timeout)
      => WaitForDataOrDisconnect((int)timeout.TotalMilliseconds, CancellationToken.None);

    public (bool Data, bool Disconnect) WaitForDataOrDisconnect(CancellationToken cancellationToken)
      => WaitForDataOrDisconnect(Timeout.Infinite, cancellationToken);

    public (bool Data, bool Disconnect) WaitForDataOrDisconnect()
      => WaitForDataOrDisconnect(Timeout.Infinite, CancellationToken.None);

    public async Task<(bool Data, bool Disconnect)> WaitForDataOrDisconnectAsync(
      int timeoutMs, CancellationToken cancellationToken)
    {
      try
      {
        int idx = await MultiPipes.WaitAnyAsync(
          timeoutMs, cancellationToken, new[] { on_data_available_, on_disconnected_ });
        return (Data: idx == 0, Disconnect: idx == 1);
      }
      catch (Exception ex) when
      (
        (ex is TimeoutException) ||
        (ex is AggregateException ae && ae.InnerException is TimeoutException)
      )
      {
        return (Data: false, Disconnect: false);
      }
    }

    public async Task<(bool Data, bool Disconnect)> WaitForDataOrDisconnectAsync(int timeoutMs)
      => await WaitForDataOrDisconnectAsync(timeoutMs, CancellationToken.None);

    public async Task<(bool Data, bool Disconnect)> WaitForDataOrDisconnectAsync(TimeSpan timeout)
      => await WaitForDataOrDisconnectAsync((int)timeout.TotalMilliseconds, CancellationToken.None);

    public async Task<(bool Data, bool Disconnect)> WaitForDataOrDisconnectAsync(CancellationToken cancellationToken)
      => await WaitForDataOrDisconnectAsync(Timeout.Infinite, cancellationToken);

    public async Task<(bool Data, bool Disconnect)> WaitForDataOrDisconnectAsync()
      => await WaitForDataOrDisconnectAsync(Timeout.Infinite, CancellationToken.None);

    public override bool CanRead => stream_.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => stream_.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override IAsyncResult BeginRead(
      byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
      return read_stream_.BeginRead(buffer, offset, count, callback, state);
    }

    public override IAsyncResult BeginWrite(
      byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
      CheckWriteOperations();
      try
      { 
        return stream_.BeginWrite(buffer, offset, count, callback, state);        
      }
      catch (IOException ioEx)
      {
        HandlePipeException(ioEx);
        throw;
      }
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        stream_.Dispose();
        read_stream_.Dispose();
        disposed_ = true;
      }
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
      return read_stream_.EndRead(asyncResult);
    }

    public override void EndWrite(IAsyncResult asyncResult)
    {
      stream_.EndWrite(asyncResult);
    }

    public override void Flush()
    {
      CheckWriteOperations();
      stream_.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
      CheckWriteOperations();
      await stream_.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      return read_stream_.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
      return read_stream_.Read(buffer);
    }

    public override async Task<int> ReadAsync(
      byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
      return await read_stream_.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer, CancellationToken cancellationToken)
    {
      return await read_stream_.ReadAsync(buffer, cancellationToken);
    }

    public override int ReadByte()
    {
      return read_stream_.ReadByte();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    
    public override void SetLength(long value) => throw new NotSupportedException();
    
    public override void Write(byte[] buffer, int offset, int count)
    {
      CheckWriteOperations();
      stream_.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
      CheckWriteOperations();
      stream_.Write(buffer);
    }

    public override async Task WriteAsync(
      byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
      CheckWriteOperations();
      await stream_.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override async ValueTask WriteAsync(
      ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
      CheckWriteOperations();
      await stream_.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
      CheckWriteOperations();
      stream_.WriteByte(value);
    }

    public override void Close()
    {
      stream_.Close();
    }

    public override void CopyTo(Stream destination, int bufferSize)
    {
      stream_.CopyTo(destination, bufferSize);
    }

    public override async Task CopyToAsync(
      Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
      await stream_.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
      await stream_.DisposeAsync();
    }

    internal async Task ConnectAsync(int timeoutMs, CancellationToken cancellationToken)
    {
      ThrowIfNotClient();
      await client_.ConnectAsync(timeoutMs, cancellationToken);
      IsConnected = client_.IsConnected;
    }

    internal async Task ConnectAsync(int timeoutMs)
    {
      ThrowIfNotClient();
      await client_.ConnectAsync(timeoutMs);
      IsConnected = client_.IsConnected;
    }

    internal async Task ConnectAsync(CancellationToken cancellationToken)
    {
      ThrowIfNotClient();
      await client_.ConnectAsync(cancellationToken);
      IsConnected = client_.IsConnected;
    }

    internal async Task ConnectAsync()
    {
      ThrowIfNotClient();
      await client_.ConnectAsync();
      IsConnected = client_.IsConnected;
    }

    internal void Connect(int timeoutMs)
    {
      ThrowIfNotClient();
      client_.Connect(timeoutMs);
      IsConnected = client_.IsConnected;
    }

    internal void Connect()
    {
      ThrowIfNotClient();
      client_.Connect();
      IsConnected = client_.IsConnected;
    }

    private void ThrowIfNotClient()
    {
      if (client_ == null && stream_ == server_)
      {
        throw new InvalidOperationException("Pipe is in server mode.");
      }
    }

    private void CheckWriteOperations()
    {
      if (disposed_)
      {
        throw new ObjectDisposedException("Stream");
      }

      if (!IsConnected)
      {
        throw new InvalidOperationException("Cannot write when pipe is disconnected");
      }
    }

    private void HandlePipeException(Exception ex)
    {
      switch (WindowsError.FromException(ex))
      {
      case WindowsErrorCode.BrokenPipe:
      case WindowsErrorCode.NoData:
      case WindowsErrorCode.PipeNotConnected:
      case WindowsErrorCode.InvalidHandle:
        IsConnected = false;
        break;
      }
    }

    /// <summary>
    /// Constantly attempts to read data from the pipe stream to maintain an up-to-date connection
    /// state.
    /// </summary>
    /// <param name="state"></param>
    private static async void DoRead(object state)
    {
      var @this = state as NamedPipeStream;
      if (@this == null)
      {
        return;
      }

      if (!@this.stream_.CanRead)
      {
        return;
      }

      byte[] buffer = new byte[MultiPipes.DefaultBufferSize];
      @this.on_connected_.Wait();

      for (; ; )
      {
        using (CancellationTokenSource cts = new())
        {
          try
          {
            cts.CancelAfter(MultiPipes.CancellationCheckIntervalMs);
            int bytesRead = await @this.stream_.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (bytesRead > 0)
            {
              @this.read_stream_.Write(buffer, 0, bytesRead);
              @this.on_data_available_.Set();
            }
            else
            {
              break;
            }
          }
          catch (OperationCanceledException ex)
          {
            // Regular check timeout occurred only if the cancellation token was cts.Token.
            if (ex.CancellationToken != cts.Token)
            {
              throw;
            }

            if (!@this.IsConnected)
            {
              break;
            }
          }
          catch (ObjectDisposedException)
          {
            // The pipe is closed.
            break;
          }
          catch (Exception ex) when
          (
            // The pipe is disconnected, waiting to connect, or the handle has not been set.
            ex is InvalidOperationException ||
            // Any I/O error occurred.
            ex is IOException
          )
          {
            if (WindowsError.FromException(ex) != WindowsErrorCode.MoreData)
            {
              break;
            }
          }
        }
      }

      @this.IsConnected = false;
      @this.read_stream_.Close();
    }
  }
}
