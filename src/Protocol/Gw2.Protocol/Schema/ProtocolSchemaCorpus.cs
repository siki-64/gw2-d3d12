using System;

namespace Gw2.Protocol.Schema
{
    public sealed class ProtocolSchemaCorpus
    {
        public ProtocolSchemaCorpus(MessageSchemaSet recv, MessageSchemaSet send, int build = 0)
        {
            Build = build;
            Recv = recv ?? throw new ArgumentNullException(nameof(recv));
            Send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public int Build { get; }

        public MessageSchemaSet Recv { get; }

        public MessageSchemaSet Send { get; }

        public MessageSchemaSet ForDirection(TrafficDirection direction)
        {
            switch (direction)
            {
                case TrafficDirection.ServerToClient: return Recv;
                case TrafficDirection.ClientToServer: return Send;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction,
                        "schema corpus requires a concrete direction");
            }
        }
    }
}
