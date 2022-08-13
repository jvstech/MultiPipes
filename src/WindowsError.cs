using System;

namespace Jvs.MultiPipes
{
  internal enum WindowsErrorCode : int
  {
    Success = 0,
    InvalidHandle = 6,
    HandleEof = 38,
    InvalidParameter = 87,
    BrokenPipe = 109,
    PipeLocal = 229,
    BadPipe = 230,
    PipeBusy = 231,
    NoData = 232,
    PipeNotConnected = 233,
    MoreData = 234
  }

  internal class WindowsError
  {
    private const int BaseHResult = unchecked((int)0x80070000);

    internal static WindowsErrorCode FromHResult(int hr)
    {
      return (WindowsErrorCode)(hr & ~BaseHResult);
    }

    internal static WindowsErrorCode FromException(Exception ex)
    {
      if (ex == null)
      {
        throw new ArgumentNullException(nameof(ex));
      }

      return FromHResult(ex.HResult);
    }
  }
}
