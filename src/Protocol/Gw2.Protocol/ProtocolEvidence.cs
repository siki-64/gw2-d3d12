namespace Gw2.Protocol
{
    public enum TrafficDirection { Unknown, ServerToClient, ClientToServer }
    public enum PayloadRepresentation { Unknown, NativeMemory, DecodedHandlerRecord, WireBytes }
    public enum EvidenceLevel { Unknown, StaticAnalysis, Observed, IndependentlyReproduced }
}
