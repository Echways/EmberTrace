using System;

namespace EmberTrace.Sessions;

public enum OverflowPolicy
{
    DropNew = 0,
    DropOldest = 1,
    StopSession = 2,
#pragma warning disable CS0618
    [Obsolete("Use DropNew instead.")]
    Drop = DropNew
#pragma warning restore CS0618
}
