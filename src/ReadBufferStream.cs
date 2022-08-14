// See: https://codereview.stackexchange.com/questions/93154/memoryqueuebufferstream

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Jvs.MultiPipes
{
  /// <summary>
  /// Stores data only until it is read, unlike a <see cref="byte[]"/> or a 
  /// <see cref="MemoryStream"/> which store data until their disposal.
  /// </summary>
  /// <remarks>
  /// This is meant to be used as a temporary storage buffer for data being read constantly from an
  /// resource such as a socket or a pipe. This allows data to be read even if the resource is
  /// disposed before the user has been able to access it.
  /// </remarks>
  public class ReadBufferStream : Stream
  {
    private class TrackedBuffer
    {
      /// <summary>
      /// Offset in `Data` from where to start reading.
      /// </summary>
      public int Index { get; set; }
      public byte[] Data { get; set; }
      public int Length => Data?.Length - Index ?? 0;

      /// <summary>
      /// Advances the read offset by `count` bytes and returns false if the end of the buffer has
      /// been reached.
      /// </summary>
      public bool Advance(int count)
      {
        Debug.Assert(count >= 0, nameof(count) + " is negative");
        if (count == 0)
        {
          return (Data.Length != 0 && Index < Data.Length);
        }

        Index += count;
        return (Index < Data.Length);
      }
    }

    private bool is_open_ = true;
    private readonly Queue<TrackedBuffer> buffers_ = new();
    private readonly ManualResetEventSlim on_data_available_ = new(false);
    private long length_;
    private object queue_lock_ = new object();

    public ReadBufferStream()
    {
    }

    internal ManualResetEventSlim OnDataAvailable => on_data_available_;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => length_;

    public override long Position
    {
      get => 0;
      set => throw new NotSupportedException();
    }

    public override void Flush()
    {
      // Do nothing.
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      // If there are no current buffers, wait for data to arrive as long as the stream is open.
      if (buffers_.Count == 0)
      {
        if (!is_open_)
        {
          return 0;
        }

        while (is_open_)
        {
          if (on_data_available_.Wait(MultiPipes.CancellationCheckIntervalMs))
          {
            break;
          }
        }

        if (!is_open_)
        {
          return 0;
        }
      }

      // At least one tracked buffer exists, so copy the data from them (up to `count` bytes) to
      // `buffer`.

      int remainingBytes = count;
      int bytesRead = 0;
      lock (queue_lock_)
      {
        while (bytesRead <= count && buffers_.Count != 0)
        {
          TrackedBuffer trackedBuffer = buffers_.Peek();
          int bytesToRead = Math.Min(trackedBuffer.Length, remainingBytes);
          if (bytesToRead <= 0)
          {
            return bytesRead;
          }

          Buffer.BlockCopy(
            trackedBuffer.Data, trackedBuffer.Index, buffer, offset + bytesRead, bytesToRead);
          bytesRead += bytesToRead;
          remainingBytes -= bytesToRead;
          if (!trackedBuffer.Advance(bytesToRead))
          {
            buffers_.Dequeue();
          }
        }

        if (buffers_.Count == 0)
        {
          if (is_open_)
          {
            // Indicate no more data is available.
            on_data_available_.Reset();
          }
          else
          {
            on_data_available_.Dispose();
          }
        }
      }

      return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) 
      => throw new NotSupportedException();

    public override void SetLength(long value) 
      => throw new NotSupportedException();

    /// <exception cref="ArgumentException"><paramref name="count"/> is less than or equal to zero
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is less than zero
    /// </exception>
    /// <exception cref="ObjectDisposedException">The stream is closed</exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
      if (count <= 0)
      {
        throw new ArgumentException(nameof(count));
      }

      if (offset < 0)
      {
        throw new ArgumentOutOfRangeException(nameof(offset));
      }

      if (!is_open_)
      {
        throw new ObjectDisposedException(nameof(ReadBufferStream));
      }

      var newBuffer = new byte[count];
      Buffer.BlockCopy(buffer, offset, newBuffer, 0, count);
      lock (queue_lock_)
      {
        buffers_.Enqueue(new TrackedBuffer() { Data = newBuffer, Index = 0 });
        length_ += count;
        on_data_available_.Set();
      }
    }

    public override void Close()
    {
      is_open_ = false;
      if (buffers_.Count == 0)
      {
        on_data_available_.Dispose();
      }
    }
  }
}
