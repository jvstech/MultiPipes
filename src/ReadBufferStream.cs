// https://codereview.stackexchange.com/questions/93154/memoryqueuebufferstream

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jvs.MultiPipes
{
  public class ReadBufferStream : Stream
  {
    private class TrackedBuffer
    {
      public int Index { get; set; }
      public byte[] Data { get; set; }
      public int Length => Data?.Length - Index ?? 0;
    }

    private bool is_open_ = true;
    private readonly Queue<TrackedBuffer> buffers_ = new();
    private readonly ManualResetEventSlim on_data_available_ = new(false);
    //private readonly CancellationTokenSource cancel_on_close_ = new();
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
      if (buffers_.Count == 0)
      {
        if (!is_open_)
        {
          return 0;
        }

        while (is_open_)
        {
          bool hasData = on_data_available_.Wait(MultiPipes.CancellationCheckIntervalMs);
          if (hasData)
          {
            break;
          }
        }

        if (!is_open_)
        {
          return 0;
        }
      }


      int remainingBytes = count;
      int bytesRead = 0;
      lock (queue_lock_)
      {
        while (bytesRead <= count && buffers_.Count != 0)
        {
          TrackedBuffer trackedBuffer = buffers_.Peek();
          int unreadLength = trackedBuffer.Length;
          int bytesToRead = Math.Min(unreadLength, remainingBytes);
          if (bytesToRead <= 0)
          {
            return bytesRead;
          }

          Buffer.BlockCopy(
            trackedBuffer.Data, trackedBuffer.Index, buffer, offset + bytesRead, bytesToRead);
          bytesRead += bytesToRead;
          remainingBytes -= bytesToRead;
          if (trackedBuffer.Index + bytesToRead >= trackedBuffer.Data.Length)
          {
            buffers_.Dequeue();
          }
          else
          {
            trackedBuffer.Index += bytesToRead;
          }
        }

        if (buffers_.Count == 0)
        {
          if (is_open_)
          {
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
      //cancel_on_close_.Cancel();
      //cancel_on_close_.Dispose();
      if (buffers_.Count == 0)
      {
        on_data_available_.Dispose();
      }
    }
  }
}
