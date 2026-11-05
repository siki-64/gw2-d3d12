using System;

namespace Gw2.Protocol.MsgPack
{
    public sealed class MsgPackTruncatedException : FormatException
    {
        public MsgPackTruncatedException(string message) : base(message) { }
    }
}
