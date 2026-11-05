namespace Gw2.Formats;

public sealed record ContentFunctionContentLayout(
    uint BindingContentType,
    int BindingInputOffset,
    int BindingFunctionOffset,
    int BindingSlotOffset,
    int InputIdOffset,
    int NodesOffset,
    int NodeCountOffset,
    uint ServerBindingSource,
    uint MovementModifierContentType,
    uint FunctionContentType)
{
    public const int NodeSize = 16;
    public const int NodeOpcodeOffset = 0;
    public const int NodeArgumentsOffset = 8;

    private static readonly CodeAnchor ServerBindingAdd = CodeAnchor.Site(
        "48 83 78 18 00 74 ?? E8 ?? ?? ?? ?? 44 8B 43 02 BA ?? 00 00 00 48 8B 88 ?? ?? ?? ?? 48 8B 01 FF 50 ?? 48 8B D8 E8 ?? ?? ?? ?? 41 B8 ?? 00 00 00 48 8B D3 48 8B 08 4C 8B 49 08", 0);
    private static readonly CodeAnchor BindingFieldsRead = CodeAnchor.Site(
        "48 85 D2 74 ?? 44 8B 4A ?? 44 89 44 24 ?? 4C 8B 42 ?? 48 8B 52 ?? E8", 0);
    private static readonly CodeAnchor InputIdRead = CodeAnchor.Site(
        "44 88 4C 24 ?? 48 8B CF 4C 8D 44 24 ?? 48 83 C2 ?? E8", 0);
    private static readonly CodeAnchor NodeStep = CodeAnchor.Site(
        "44 8B 73 ?? 44 3B 75 ?? 72 ?? 41 B8 ?? 00 00 00 48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 49 C1 E6 04 4C 03 75 ??", 0);

    private static readonly CodeAnchor MovementModifierAdd = CodeAnchor.Site(
        "44 8B 43 06 BA ?? ?? 00 00 48 8B 88 E0 00 00 00 48 8B 01 FF 50 58 48 8B F0 48 85 C0 75 ?? 41 B8", 0);

    private static readonly CodeAnchor ServerFunctionLookup = CodeAnchor.Site(
        "44 8B 46 06 BA ?? 00 00 00 48 8B 88 E0 00 00 00 48 8B 01 FF 50 58 8B 56 02 48 8D 4F", 0);

    private static ContentFunctionContentLayout? _current;

    public static ContentFunctionContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static ContentFunctionContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static ContentFunctionContentLayout FromExecutable(ExecutableImage executable)
    {
        var bindingAdd = ServerBindingAdd.Locate(executable);
        var bindingFields = BindingFieldsRead.LocateAll(executable)[0];
        var inputId = InputIdRead.Locate(executable);
        var nodeStep = NodeStep.Locate(executable);
        return new(
            executable.Dword(bindingAdd + 17),
            executable.Bytes(bindingFields + 21, 1)[0],
            executable.Bytes(bindingFields + 17, 1)[0],
            executable.Bytes(bindingFields + 8, 1)[0],
            executable.Bytes(inputId + 16, 1)[0],
            executable.Bytes(nodeStep + 42, 1)[0],
            executable.Bytes(nodeStep + 7, 1)[0],
            executable.Dword(bindingAdd + 44),
            executable.Dword(MovementModifierAdd.Locate(executable) + 5),
            executable.Dword(ServerFunctionLookup.Locate(executable) + 5));
    }
}
