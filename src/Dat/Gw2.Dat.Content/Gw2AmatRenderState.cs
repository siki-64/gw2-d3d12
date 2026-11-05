namespace Gw2.Dat.Content;

public enum Gw2BlendFactor
{
    None,
    Zero,
    One,
    SourceColor,
    InverseSourceColor,
    SourceAlpha,
    InverseSourceAlpha,
    DestinationAlpha,
    InverseDestinationAlpha,
    DestinationColor,
    InverseDestinationColor,
    SourceAlphaSaturate,
    BlendFactor,
    InverseBlendFactor,
}

public enum Gw2BlendEquation
{
    Add,
    Subtract,
    ReverseSubtract,
    Min,
    Max,
}

public enum Gw2CullMode
{
    None,
    Clockwise,
    CounterClockwise,
}

public readonly record struct Gw2AmatRenderState(ulong Flags)
{
    private const int BlendShift = 12;
    private const int BlendEquationShift = 28;
    private const int CullShift = 36;
    private const ulong FactorMask = 0xF;
    private const ulong EquationMask = 0x7;
    private const ulong CullMask = 0x3;
    private const int AlphaReferenceShift = 40;
    private const ulong AlphaReferenceMask = 0xFF;
    private const float AlphaReferenceScale = 255f;

    public bool IsBlended => ((Flags >> BlendShift) & 0xFFFF) != 0;

    public Gw2BlendFactor ColorSource => FactorAt(0);

    public Gw2BlendFactor ColorDestination => FactorAt(4);

    public Gw2BlendFactor AlphaSource => FactorAt(8);

    public Gw2BlendFactor AlphaDestination => FactorAt(12);

    public Gw2BlendEquation ColorEquation => (Gw2BlendEquation)((Flags >> BlendEquationShift) & EquationMask);

    public Gw2BlendEquation AlphaEquation => (Gw2BlendEquation)((Flags >> (BlendEquationShift + 3)) & EquationMask);

    public Gw2CullMode Cull => (Gw2CullMode)((Flags >> CullShift) & CullMask);

    public float AlphaReference => ((Flags >> AlphaReferenceShift) & AlphaReferenceMask) / AlphaReferenceScale;

    private Gw2BlendFactor FactorAt(int offset) => (Gw2BlendFactor)((Flags >> (BlendShift + offset)) & FactorMask);
}
