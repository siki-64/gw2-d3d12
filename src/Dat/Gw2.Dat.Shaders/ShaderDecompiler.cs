using System.Numerics;
using System.Globalization;
using System.Text;
using Gw2.Dat.Content;

namespace Gw2.Dat.Shaders;

public sealed partial class ShaderDecompiler
{
    private const string Components = "xyzw";
    private const int ConstantRegisterBytes = 16;
    private const int MatrixUniformType = 4;
    private const int MatrixColumns = 4;

    private static readonly HashSet<string> HlslReservedNames = new(StringComparer.Ordinal)
    {
        "abs", "all", "any", "asfloat", "asint", "asuint", "ceil", "clamp", "cos", "cross", "ddx", "ddy", "distance", "dot",
        "exp", "exp2", "floor", "fmod", "frac", "length", "lerp", "lit", "log", "log2", "max", "min", "mul", "normalize",
        "pow", "rcp", "reflect", "refract", "round", "rsqrt", "saturate", "sign", "sin", "smoothstep", "sqrt", "step", "tan",
        "trunc", "input", "output", "main", "sample", "texture", "sampler", "matrix", "vector", "line", "point", "triangle",
        "precise", "shared", "static", "uniform", "linear", "centroid", "nointerpolation", "noperspective", "register",
        "packoffset", "string", "pass", "technique", "compile", "in", "out", "inout",
    };

    private readonly Gw2Sm5Program _program;
    private readonly Gw2BgfxShaderBinary _binary;
    private readonly IReadOnlyList<Gw2DxbcSignatureElement> _inputs;
    private readonly IReadOnlyList<Gw2DxbcSignatureElement> _outputs;
    private readonly Dictionary<int, Gw2ShaderResourceDimension> _resourceDimensions = [];
    private readonly Dictionary<int, int> _resourceReturnTypes = [];
    private readonly HashSet<string> _matrixUniformsKeptAsRegisters = [];
    private readonly HashSet<int> _comparisonSamplers = [];
    private readonly List<string> _globalNames = [];
    private ShaderCompilerTarget _target;
    private readonly Dictionary<int, int> _constantBufferSizes = [];
    private readonly Dictionary<int, int> _indexableTempSizes = [];
    private int _temps;

    private ShaderDecompiler(Gw2Sm5Program program, Gw2BgfxShaderBinary binary, IReadOnlyList<Gw2DxbcSignatureElement> inputs, IReadOnlyList<Gw2DxbcSignatureElement> outputs)
    {
        _program = program;
        _binary = binary;
        _inputs = inputs;
        _outputs = outputs;
    }

    private enum ValueType
    {
        Float,
        Int,
        Uint,
        Bool,
    }

    public static string Decompile(Gw2BgfxShaderBinary binary, ShaderCompilerTarget target = ShaderCompilerTarget.Fxc) => DecompileShader(binary, target).Text;

    public static DecompiledShader DecompileShader(Gw2BgfxShaderBinary binary, ShaderCompilerTarget target = ShaderCompilerTarget.Fxc)
    {
        ArgumentNullException.ThrowIfNull(binary);
        var container = binary.Container ?? throw new InvalidDataException("no DXBC container");
        if (!container.TryGetChunk("SHEX", out var shex) && !container.TryGetChunk("SHDR", out shex)) throw new InvalidDataException("no SHEX chunk");
        var program = Gw2Sm5Program.TryParse(shex.Data) ?? throw new InvalidDataException("SHEX does not decode");
        var inputs = container.TryGetChunk("ISGN", out var isgn) ? Gw2DxbcSignature.TryParse(isgn.Data) ?? [] : [];
        var outputs = container.TryGetChunk("OSGN", out var osgn) ? Gw2DxbcSignature.TryParse(osgn.Data) ?? [] : [];
        var decompiler = new ShaderDecompiler(program, binary, inputs, outputs) { _target = target };
        var text = decompiler.Write();
        return new(text, decompiler._globalNames);
    }

    private string Write()
    {
        foreach (var instruction in _program.Instructions) Declare(instruction);
        RecogniseIdioms();
        ChooseMatrixUniforms();
        for (var index = 0; index < _program.Instructions.Count; index++)
            if (!Gw2Sm5OpCodes.IsDeclaration(_program.Instructions[index].OpCode) && _program.Instructions[index].OpCode != Gw2Sm5OpCodes.CustomData)
                TranslateIdiom(index);

        var text = new StringBuilder();
        WriteConstantBuffers(text);
        WriteResources(text);
        WriteSignatureStruct(text, "ShaderInput", _inputs);
        if (!WritesLiterally) WriteSignatureStruct(text, "ShaderOutput", _outputs);

        _globalNames.AddRange(WritesLiterally ? ["ShaderInput", "main"] : ["ShaderInput", "ShaderOutput", "main"]);
        if (_usesAwayFromZero) _globalNames.Add("AwayFromZero");
        if (_usesAwayFromZero) text.Append(AwayFromZeroFunction);
        if (WritesLiterally) text.Append(CultureInfo.InvariantCulture, $"void main({LiteralParameters()})\n{{\n");
        else text.Append("ShaderOutput main(ShaderInput input)\n{\n    ShaderOutput output = (ShaderOutput)0;\n");
        var (body, variables) = NameVariables(RenderBody());
        if (HasVariables)
            foreach (var (name, width, type) in variables) text.Append(CultureInfo.InvariantCulture, $"    {ValueTypeName(type)}{WidthSuffix(width)} {name} = 0;\n");
        else
            for (var temp = 0; temp < _temps; temp++) text.Append(CultureInfo.InvariantCulture, $"    float4 r{temp} = 0;\n");
        foreach (var (register, size) in _indexableTempSizes) text.Append(CultureInfo.InvariantCulture, $"    float4 x{register}[{size}];\n");
        if (!WritesLiterally)
        {
            foreach (var register in _inputs.Select(element => element.Register).Distinct()) text.Append(CultureInfo.InvariantCulture, $"    float4 v{register} = 0;\n");
            foreach (var register in OutputRegisters()) text.Append(CultureInfo.InvariantCulture, $"    float4 o{register} = 0;\n");
            if (_outputs.Any(IsDepthOutput)) text.Append("    float oDepth = 0;\n");
            foreach (var element in _inputs)
                text.Append(CultureInfo.InvariantCulture, $"    v{element.Register}.{MaskText(element.Mask)} = {Convert($"input.{MemberName(element)}", ElementType(element), ValueType.Float)};\n");
        }

        text.Append(body);
        text.Append("}\n");
        return text.ToString();
    }

    private void Declare(Gw2Sm5Instruction instruction)
    {
        var operand = instruction.Operands.Count > 0 ? instruction.Operands[0] : null;
        switch (instruction.Name)
        {
            case "dcl_globalFlags":
                _skipsOptimization = (instruction.Controls & SkipOptimizationGlobalFlag) != 0;
                break;
            case "dcl_temps":
                _temps = (int)instruction.Extra[0];
                break;
            case "dcl_indexableTemp":
                _indexableTempSizes[(int)instruction.Extra[0]] = (int)instruction.Extra[1];
                break;
            case "dcl_constantbuffer" when operand is not null:
                _constantBufferSizes[(int)operand.Indices[0].Immediate] = (int)operand.Indices[1].Immediate;
                break;
            case "dcl_resource" when operand is not null:
                _resourceDimensions[operand.FirstIndex] = (Gw2ShaderResourceDimension)((instruction.Token >> 11) & 0x1F);
                _resourceReturnTypes[operand.FirstIndex] = instruction.Extra.Count > 0 ? (int)(instruction.Extra[0] & 0xF) : 5;
                break;
            case "dcl_sampler" when operand is not null:
                if (((instruction.Token >> 11) & 0xF) == 1) _comparisonSamplers.Add(operand.FirstIndex);
                break;
        }
    }

    private IEnumerable<int> OutputRegisters() => _outputs.Where(element => !IsDepthOutput(element)).Select(element => element.Register).Distinct();

    private static bool IsDepthOutput(Gw2DxbcSignatureElement element) => element.SemanticName.StartsWith("SV_Depth", StringComparison.OrdinalIgnoreCase);

    private void WriteConstantBuffers(StringBuilder text)
    {
        foreach (var (slot, size) in _constantBufferSizes.OrderBy(pair => pair.Key))
        {
            _globalNames.Add($"ConstantBuffer{slot}");
            text.Append(CultureInfo.InvariantCulture, $"cbuffer ConstantBuffer{slot} : register(b{slot})\n{{\n");
            var covered = new bool[size];
            if (slot == 0)
            {
                foreach (var uniform in ConstantUniforms())
                {
                    var start = uniform.Register / ConstantRegisterBytes;
                    if (start >= size) continue;
                    var count = Math.Min(Math.Max(uniform.Count, 1), size - start);
                    for (var register = start; register < start + count; register++) covered[register] = true;
                    if (IsMatrix(uniform))
                    {
                        var matrices = (count + MatrixColumns - 1) / MatrixColumns;
                        var matrixArray = matrices > 1 ? $"[{matrices}]" : string.Empty;
                        _globalNames.Add(UniformName(uniform));
                        text.Append(CultureInfo.InvariantCulture, $"    float4x4 {UniformName(uniform)}{matrixArray} : packoffset(c{start});\n");
                        continue;
                    }

                    var array = uniform.Count > 1 ? $"[{count}]" : string.Empty;
                    _globalNames.Add(UniformName(uniform));
                    text.Append(CultureInfo.InvariantCulture, $"    float4 {UniformName(uniform)}{array} : packoffset(c{start});\n");
                }
            }

            for (var register = 0; register < size; register++)
                if (!covered[register])
                {
                    _globalNames.Add($"cb{slot}_c{register}");
                    text.Append(CultureInfo.InvariantCulture, $"    float4 cb{slot}_c{register} : packoffset(c{register});\n");
                }
            text.Append("}\n\n");
        }

        if (_program.ImmediateConstantBuffer.Count > 0)
        {
            var values = _program.ImmediateConstantBuffer;
            _globalNames.Add("icb");
            text.Append("static const uint4 icb[] =\n{\n");
            for (var index = 0; index + 3 < values.Count; index += 4)
                text.Append(CultureInfo.InvariantCulture, $"    uint4(0x{values[index]:x8}, 0x{values[index + 1]:x8}, 0x{values[index + 2]:x8}, 0x{values[index + 3]:x8}),\n");
            text.Append("};\n\n");
        }
    }

    private static string UniformName(Gw2ShaderReflectionEntry uniform) =>
        HlslReservedNames.Contains(uniform.Name) ? uniform.Name + "Constant" : uniform.Name;

    private IEnumerable<Gw2ShaderReflectionEntry> ConstantUniforms() =>
        _binary.Uniforms.Where(uniform => uniform.Kind == Gw2ShaderReflectionKind.Constant)
            .GroupBy(uniform => uniform.Register).Select(group => group.First()).OrderBy(uniform => uniform.Register);

    private void WriteResources(StringBuilder text)
    {
        foreach (var (register, dimension) in _resourceDimensions.OrderBy(pair => pair.Key))
        {
            _globalNames.Add(TextureName(register));
            text.Append(CultureInfo.InvariantCulture, $"{TextureTypeName(dimension)}<{ReturnTypeName(_resourceReturnTypes[register])}4> {TextureName(register)} : register(t{register});\n");
        }

        foreach (var register in SamplerRegisters())
        {
            _globalNames.Add(SamplerName(register));
            text.Append(CultureInfo.InvariantCulture, $"{(_comparisonSamplers.Contains(register) ? "SamplerComparisonState" : "SamplerState")} {SamplerName(register)} : register(s{register});\n");
        }

        text.Append('\n');
    }

    private IEnumerable<int> SamplerRegisters() =>
        _program.Instructions.Where(instruction => instruction.Name == "dcl_sampler").Select(instruction => instruction.Operands[0].FirstIndex).OrderBy(register => register);

    private string? UniformSamplerName(int register) =>
        _binary.Uniforms.FirstOrDefault(uniform => uniform.Kind == Gw2ShaderReflectionKind.Sampler && uniform.Register == register)?.Name;

    private string SamplerName(int register) => UniformSamplerName(register) ?? $"sampler{register}";

    private string TextureName(int register) => UniformSamplerName(register) is { } name ? name + "Texture" : $"texture{register}";

    private void WriteSignatureStruct(StringBuilder text, string name, IReadOnlyList<Gw2DxbcSignatureElement> elements)
    {
        text.Append(CultureInfo.InvariantCulture, $"struct {name}\n{{\n");
        foreach (var element in elements)
        {
            var count = BitCount(element.Mask);
            var type = ValueTypeName(ElementType(element));
            text.Append(CultureInfo.InvariantCulture, $"    {type}{(count > 1 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty)} {MemberName(element)} : {element.SemanticName}{(element.SemanticName.StartsWith("SV_", StringComparison.OrdinalIgnoreCase) && element.SemanticIndex == 0 ? string.Empty : element.SemanticIndex.ToString(CultureInfo.InvariantCulture))};\n");
        }

        text.Append("};\n\n");
    }

    private static string MemberName(Gw2DxbcSignatureElement element) =>
        element.SemanticName.Replace("SV_", string.Empty, StringComparison.OrdinalIgnoreCase) + element.SemanticIndex.ToString(CultureInfo.InvariantCulture);

    private static ValueType ElementType(Gw2DxbcSignatureElement element) => element.ComponentType switch
    {
        1 => ValueType.Uint,
        2 => ValueType.Int,
        _ => ValueType.Float,
    };

    private static int BitCount(int mask) => System.Numerics.BitOperations.PopCount((uint)mask);

    private static string MaskText(int mask) => string.Concat(Enumerable.Range(0, 4).Where(component => (mask & (1 << component)) != 0).Select(component => Components[component]));


    private void TranslateInstruction(Gw2Sm5Instruction instruction)
    {
        var operands = instruction.Operands;
        switch (instruction.Name)
        {
            case "mov": Assign(instruction, ValueType.Float, ValueType.Float, sources => sources[0]); break;
            case "movc": Assign(instruction, ValueType.Float, ValueType.Float, sources => SelectText(sources[0], sources[1], sources[2], Width(instruction)), ValueType.Uint); break;
            case "add": Binary(instruction, ValueType.Float, "+"); break;
            case "mul": Binary(instruction, ValueType.Float, "*"); break;
            case "div": Binary(instruction, ValueType.Float, "/"); break;
            case "mad": Assign(instruction, ValueType.Float, ValueType.Float, sources => $"{sources[0]} * {sources[1]} + {sources[2]}"); break;
            case "min": Call(instruction, ValueType.Float, "min"); break;
            case "max": Call(instruction, ValueType.Float, "max"); break;
            case "dp2": Dot(instruction, 2); break;
            case "dp3": Dot(instruction, 3); break;
            case "dp4": Dot(instruction, 4); break;
            case "exp": Call(instruction, ValueType.Float, "exp2"); break;
            case "log": Call(instruction, ValueType.Float, "log2"); break;
            case "rsq": Call(instruction, ValueType.Float, "rsqrt"); break;
            case "sqrt": Call(instruction, ValueType.Float, "sqrt"); break;
            case "rcp": Call(instruction, ValueType.Float, "rcp"); break;
            case "frc": Call(instruction, ValueType.Float, "frac"); break;
            case "round_ne": Call(instruction, ValueType.Float, "round"); break;
            case "round_ni": Call(instruction, ValueType.Float, "floor"); break;
            case "round_pi": Call(instruction, ValueType.Float, "ceil"); break;
            case "round_z": Call(instruction, ValueType.Float, "trunc"); break;
            case "deriv_rtx" or "deriv_rtx_coarse" or "deriv_rtx_fine": Call(instruction, ValueType.Float, instruction.Name.Replace("deriv_rtx", "ddx", StringComparison.Ordinal)); break;
            case "deriv_rty" or "deriv_rty_coarse" or "deriv_rty_fine": Call(instruction, ValueType.Float, instruction.Name.Replace("deriv_rty", "ddy", StringComparison.Ordinal)); break;
            case "sincos": SinCos(instruction); break;
            case "eq": Compare(instruction, ValueType.Float, "=="); break;
            case "ne": Compare(instruction, ValueType.Float, "!="); break;
            case "lt": Compare(instruction, ValueType.Float, "<"); break;
            case "ge": Compare(instruction, ValueType.Float, ">="); break;
            case "ieq": Compare(instruction, ValueType.Int, "=="); break;
            case "ine": Compare(instruction, ValueType.Int, "!="); break;
            case "ilt": Compare(instruction, ValueType.Int, "<"); break;
            case "ige": Compare(instruction, ValueType.Int, ">="); break;
            case "ult": Compare(instruction, ValueType.Uint, "<"); break;
            case "uge": Compare(instruction, ValueType.Uint, ">="); break;
            case "itof": Assign(instruction, ValueType.Int, ValueType.Float, sources => $"(float{WidthSuffix(Width(instruction))}){sources[0]}"); break;
            case "utof": Assign(instruction, ValueType.Uint, ValueType.Float, sources => $"(float{WidthSuffix(Width(instruction))}){sources[0]}"); break;
            case "ftoi": Assign(instruction, ValueType.Float, ValueType.Int, sources => $"(int{WidthSuffix(Width(instruction))}){sources[0]}"); break;
            case "ftou": Assign(instruction, ValueType.Float, ValueType.Uint, sources => $"(uint{WidthSuffix(Width(instruction))}){sources[0]}"); break;
            case "iadd": Binary(instruction, ValueType.Int, "+"); break;
            case "imad": Assign(instruction, ValueType.Int, ValueType.Int, sources => $"{sources[0]} * {sources[1]} + {sources[2]}"); break;
            case "umad": Assign(instruction, ValueType.Uint, ValueType.Uint, sources => $"{sources[0]} * {sources[1]} + {sources[2]}"); break;
            case "imax": Call(instruction, ValueType.Int, "max"); break;
            case "imin": Call(instruction, ValueType.Int, "min"); break;
            case "umax": Call(instruction, ValueType.Uint, "max"); break;
            case "umin": Call(instruction, ValueType.Uint, "min"); break;
            case "ineg": Assign(instruction, ValueType.Int, ValueType.Int, sources => $"-({sources[0]})"); break;
            case "and": Binary(instruction, ValueType.Uint, "&"); break;
            case "or": Binary(instruction, ValueType.Uint, "|"); break;
            case "xor": Binary(instruction, ValueType.Uint, "^"); break;
            case "not": Assign(instruction, ValueType.Uint, ValueType.Uint, sources => $"~{sources[0]}"); break;
            case "ishl": Binary(instruction, ValueType.Int, "<<"); break;
            case "ishr": Binary(instruction, ValueType.Int, ">>"); break;
            case "ushr": Binary(instruction, ValueType.Uint, ">>"); break;
            case "imul": WideMultiply(instruction, ValueType.Int); break;
            case "umul": WideMultiply(instruction, ValueType.Uint); break;
            case "udiv": UnsignedDivide(instruction); break;
            case "countbits": Call(instruction, ValueType.Uint, "countbits"); break;
            case "firstbit_hi": Call(instruction, ValueType.Uint, "firstbithigh"); break;
            case "firstbit_shi": Call(instruction, ValueType.Int, "firstbithigh"); break;
            case "firstbit_lo": Call(instruction, ValueType.Uint, "firstbitlow"); break;
            case "bfrev": Call(instruction, ValueType.Uint, "reversebits"); break;
            case "f32tof16": Assign(instruction, ValueType.Float, ValueType.Uint, sources => $"f32tof16({sources[0]})"); break;
            case "f16tof32": Assign(instruction, ValueType.Uint, ValueType.Float, sources => $"f16tof32({sources[0]})"); break;
            case "ubfe": Assign(instruction, ValueType.Uint, ValueType.Uint, sources => $"({sources[2]} << (32 - ({sources[0]} & 31) - ({sources[1]} & 31))) >> (32 - ({sources[0]} & 31))"); break;
            case "ibfe": Assign(instruction, ValueType.Int, ValueType.Int, sources => $"({sources[2]} << (32 - ({sources[0]} & 31) - ({sources[1]} & 31))) >> (32 - ({sources[0]} & 31))"); break;
            case "bfi": Assign(instruction, ValueType.Uint, ValueType.Uint, sources => $"(({sources[2]} << ({sources[1]} & 31)) & ((((1u << ({sources[0]} & 31)) - 1u)) << ({sources[1]} & 31))) | ({sources[3]} & ~((((1u << ({sources[0]} & 31)) - 1u)) << ({sources[1]} & 31)))"); break;
            case "if": Line("[branch]"); Line($"if ({Test(instruction, operands[0])})"); Line("{"); _depth++; break;
            case "else": _depth--; Line("}"); Line("else"); Line("{"); _depth++; break;
            case "endif" or "endloop" or "endswitch": _depth--; Line("}"); break;
            case "loop": Line("[loop]"); _statements.Add(NewStatement("while (true)", opensLoop: true)); Line("{"); _depth++; break;
            case "break": Line("break;"); break;
            case "breakc": BreakIf(Test(instruction, operands[0])); break;
            case "continue": Line("continue;"); break;
            case "continuec": Line($"if ({Test(instruction, operands[0])}) continue;"); break;
            case "switch": Line($"switch ({Source(operands[0], [0], ValueType.Int)})"); Line("{"); _depth++; break;
            case "case": Line($"case {Source(operands[0], [0], ValueType.Int)}:"); break;
            case "default": Line("default:"); break;
            case "discard": Line($"if ({Test(instruction, operands[0])}) discard;"); break;
            case "ret": WriteReturn(); break;
            case "retc": Line($"if ({Test(instruction, operands[0])})"); Line("{"); _depth++; WriteReturn(); _depth--; Line("}"); break;
            case "sample": Sample(instruction, "Sample", 0); break;
            case "sample_l": Sample(instruction, "SampleLevel", 1); break;
            case "sample_b": Sample(instruction, "SampleBias", 1); break;
            case "sample_d": Sample(instruction, "SampleGrad", 2); break;
            case "sample_c": Sample(instruction, "SampleCmp", 1); break;
            case "sample_c_lz": Sample(instruction, "SampleCmpLevelZero", 1); break;
            case "gather4": Sample(instruction, GatherName(operands[3]), 0); break;
            case "lod": Lod(instruction); break;
            case "ld": Load(instruction); break;
            case "resinfo": ResourceInfo(instruction); break;
            case "nop" or "sync": break;
            default: throw new NotSupportedException($"opcode {instruction.Name} is not decompiled yet");
        }
    }

    private static int Width(Gw2Sm5Instruction instruction) => Math.Max(1, DestinationComponents(instruction.Operands[0]).Count);

    private static string WidthSuffix(int width) => width > 1 ? width.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static IReadOnlyList<int> DestinationComponents(Gw2Sm5Operand destination)
    {
        if (destination.ComponentCount != 4) return [0];
        return Enumerable.Range(0, 4).Where(component => (destination.Mask & (1 << component)) != 0).ToArray();
    }

    private void Assign(Gw2Sm5Instruction instruction, ValueType sourceType, ValueType resultType, Func<string[], string> expression, ValueType? firstSourceType = null)
    {
        var destination = instruction.Operands[0];
        if (destination.RegisterType == Gw2ShaderRegisterType.Null) return;
        var components = DestinationComponents(destination);
        var sources = instruction.Operands.Skip(1)
            .Select((operand, index) => Source(operand, components, index == 0 && firstSourceType is { } first ? first : sourceType, firstSourceType is not null && index == 0))
            .ToArray();
        Store(instruction, destination, components, expression(sources), resultType);
    }

    private void Store(Gw2Sm5Instruction instruction, Gw2Sm5Operand destination, IReadOnlyList<int> components, string expression, ValueType resultType) =>
        EmitAssignment(instruction, destination, components, expression, resultType);

    private void Binary(Gw2Sm5Instruction instruction, ValueType type, string symbol) =>
        Assign(instruction, type, type, sources => $"{sources[0]} {symbol} {sources[1]}");

    private void Call(Gw2Sm5Instruction instruction, ValueType type, string function) =>
        Assign(instruction, type, type, sources => $"{function}({string.Join(", ", sources)})");

    private void Compare(Gw2Sm5Instruction instruction, ValueType type, string symbol)
    {
        var destination = _idiomDestinations.GetValueOrDefault(_currentReader, instruction.Operands[0]);
        if (destination.RegisterType == Gw2ShaderRegisterType.Null) return;
        var components = DestinationComponents(destination);
        var condition = $"{Source(instruction.Operands[1], components, type)} {symbol} {Source(instruction.Operands[2], components, type)}";
        EmitAssignment(instruction, destination, components, SelectText($"({condition})", "0xffffffffu", "0u", components.Count), ValueType.Uint, condition: condition);
    }

    private void Dot(Gw2Sm5Instruction instruction, int count)
    {
        var destination = instruction.Operands[0];
        var components = Enumerable.Range(0, count).ToArray();
        var left = Source(instruction.Operands[1], components, ValueType.Float);
        var right = Source(instruction.Operands[2], components, ValueType.Float);
        EmitAssignment(instruction, destination, DestinationComponents(destination), $"dot({left}, {right})", ValueType.Float, scalarValue: true);
    }

    private void SinCos(Gw2Sm5Instruction instruction)
    {
        for (var output = 0; output < 2; output++)
        {
            var destination = instruction.Operands[output];
            if (destination.RegisterType == Gw2ShaderRegisterType.Null) continue;
            var components = DestinationComponents(destination);
            Store(instruction, destination, components, $"{(output == 0 ? "sin" : "cos")}({Source(instruction.Operands[2], components, ValueType.Float)})", ValueType.Float);
        }
    }

    private void WideMultiply(Gw2Sm5Instruction instruction, ValueType type)
    {
        var destination = instruction.Operands[1];
        if (instruction.Operands[0].RegisterType != Gw2ShaderRegisterType.Null) throw new NotSupportedException("high half of a wide multiply");
        if (destination.RegisterType == Gw2ShaderRegisterType.Null) return;
        var components = DestinationComponents(destination);
        Store(instruction, destination, components, $"{Source(instruction.Operands[2], components, type)} * {Source(instruction.Operands[3], components, type)}", type);
    }

    private void UnsignedDivide(Gw2Sm5Instruction instruction)
    {
        for (var output = 0; output < 2; output++)
        {
            var destination = instruction.Operands[output];
            if (destination.RegisterType == Gw2ShaderRegisterType.Null) continue;
            var components = DestinationComponents(destination);
            Store(instruction, destination, components, $"{Source(instruction.Operands[2], components, ValueType.Uint)} {(output == 0 ? "/" : "%")} {Source(instruction.Operands[3], components, ValueType.Uint)}", ValueType.Uint);
        }
    }

    private string Test(Gw2Sm5Instruction instruction, Gw2Sm5Operand operand) =>
        instruction.TestNonZero ? Source(operand, [0], ValueType.Uint, asCondition: true) : $"{Source(operand, [0], ValueType.Uint, asCondition: true)} == false";

    private const int SkipOptimizationGlobalFlag = 1 << 4;

    private bool _skipsOptimization;

    private bool WritesLiterally =>
        _skipsOptimization
        && _outputs.All(element => ElementType(element) == ValueType.Float && element.Mask == (1 << BitCount(element.Mask)) - 1)
        && _outputs.Select(element => IsDepthOutput(element) ? -1 : element.Register).Distinct().Count() == _outputs.Count;

    private string LiteralParameters() => string.Join(", ",
        new[] { "ShaderInput input" }
            .Concat(_outputs.Select(element => $"out {FloatOfWidth(element)} {(IsDepthOutput(element) ? "oDepth" : $"o{element.Register}")} : {SemanticText(element)}")));

    private (string Member, ValueType Type)? LiteralInputSource(Gw2Sm5Operand operand, IReadOnlyList<int> components)
    {
        if (operand.RegisterType != Gw2ShaderRegisterType.Input || operand.ComponentCount != 4 || operand.Indices.Count != 1) return null;
        var registerComponents = components.Select(component => operand.Swizzle[component]).ToArray();
        var element = _inputs.FirstOrDefault(candidate => candidate.Register == operand.FirstIndex && registerComponents.All(component => (candidate.Mask & (1 << component)) != 0));
        if (element is null) return null;
        var firstComponent = BitOperations.TrailingZeroCount(element.Mask);
        var member = $"input.{MemberName(element)}";
        if (BitCount(element.Mask) == 1 && registerComponents.Length == 1) return (member, ElementType(element));
        return ($"{member}.{string.Concat(registerComponents.Select(component => Components[component - firstComponent]))}", ElementType(element));
    }

    private static string FloatOfWidth(Gw2DxbcSignatureElement element) =>
        BitCount(element.Mask) > 1 ? $"float{BitCount(element.Mask)}" : "float";

    private static string SemanticText(Gw2DxbcSignatureElement element) =>
        element.SemanticName + (element.SemanticName.StartsWith("SV_", StringComparison.OrdinalIgnoreCase) && element.SemanticIndex == 0 ? string.Empty : element.SemanticIndex.ToString(CultureInfo.InvariantCulture));

    private void WriteReturn()
    {
        if (WritesLiterally)
        {
            Line("return;");
            return;
        }

        foreach (var element in _outputs)
        {
            var value = IsDepthOutput(element) ? "oDepth" : $"o{element.Register}.{MaskText(element.Mask)}";
            Line($"output.{MemberName(element)} = {Convert(value, ValueType.Float, ElementType(element))};");
        }

        Line("return output;");
    }

    private static int CoordinateCount(Gw2ShaderResourceDimension dimension) => dimension switch
    {
        Gw2ShaderResourceDimension.Buffer or Gw2ShaderResourceDimension.Texture1D => 1,
        Gw2ShaderResourceDimension.Texture2D or Gw2ShaderResourceDimension.Texture1DArray or Gw2ShaderResourceDimension.Texture2DMultisampled => 2,
        Gw2ShaderResourceDimension.TextureCubeArray => 4,
        _ => 3,
    };

    private Gw2ShaderResourceDimension DimensionOf(Gw2Sm5Instruction instruction, Gw2Sm5Operand resource) =>
        instruction.ResourceDimension ?? (_resourceDimensions.TryGetValue(resource.FirstIndex, out var dimension) ? dimension : Gw2ShaderResourceDimension.Texture2D);

    private static string GatherName(Gw2Sm5Operand sampler) => sampler.Swizzle[0] switch
    {
        1 => "GatherGreen",
        2 => "GatherBlue",
        3 => "GatherAlpha",
        _ => "GatherRed",
    };

    private void Sample(Gw2Sm5Instruction instruction, string method, int extraArguments)
    {
        var destination = instruction.Operands[0];
        var resource = instruction.Operands[2];
        var sampler = instruction.Operands[3];
        var coordinates = Enumerable.Range(0, CoordinateCount(DimensionOf(instruction, resource))).ToArray();
        var arguments = new List<string> { SamplerName(sampler.FirstIndex), Source(instruction.Operands[1], coordinates, ValueType.Float) };
        if (extraArguments == 2)
        {
            arguments.Add(Source(instruction.Operands[4], coordinates, ValueType.Float));
            arguments.Add(Source(instruction.Operands[5], coordinates, ValueType.Float));
        }
        else if (extraArguments == 1)
        {
            arguments.Add(Source(instruction.Operands[4], [0], ValueType.Float));
        }

        if (instruction.SampleOffsets is { } offsets && (offsets.U != 0 || offsets.V != 0 || offsets.W != 0))
            arguments.Add(OffsetText(offsets, coordinates.Length));
        StoreResourceResult(instruction, destination, resource, $"{TextureName(resource.FirstIndex)}.{method}({string.Join(", ", arguments)})");
    }

    private static string OffsetText(Gw2Sm5SampleOffsets offsets, int count)
    {
        var values = new[] { offsets.U, offsets.V, offsets.W }.Take(Math.Min(count, 3)).Select(value => value.ToString(CultureInfo.InvariantCulture));
        return count > 1 ? $"int{Math.Min(count, 3)}({string.Join(", ", values)})" : values.First();
    }

    private void StoreResourceResult(Gw2Sm5Instruction instruction, Gw2Sm5Operand destination, Gw2Sm5Operand resource, string call)
    {
        if (destination.RegisterType == Gw2ShaderRegisterType.Null) return;
        var components = DestinationComponents(destination);
        var swizzle = string.Concat(components.Select(component => Components[resource.Swizzle[component]]));
        var resultType = _resourceReturnTypes.TryGetValue(resource.FirstIndex, out var returnType) ? ReturnValueType(returnType) : ValueType.Float;
        Store(instruction, destination, components, $"{call}.{swizzle}", resultType);
    }

    private void Lod(Gw2Sm5Instruction instruction)
    {
        var destination = instruction.Operands[0];
        var resource = instruction.Operands[2];
        var coordinates = Enumerable.Range(0, CoordinateCount(DimensionOf(instruction, resource))).ToArray();
        var texture = TextureName(resource.FirstIndex);
        var sampler = SamplerName(instruction.Operands[3].FirstIndex);
        var coordinate = Source(instruction.Operands[1], coordinates, ValueType.Float);
        var components = DestinationComponents(destination);
        var pair = $"float4({texture}.CalculateLevelOfDetail({sampler}, {coordinate}), {texture}.CalculateLevelOfDetailUnclamped({sampler}, {coordinate}), 0, 0)";
        Store(instruction, destination, components, $"{pair}.{string.Concat(components.Select(component => Components[instruction.Operands[2].Swizzle[component]]))}", ValueType.Float);
    }

    private void Load(Gw2Sm5Instruction instruction)
    {
        var resource = instruction.Operands[2];
        var dimension = DimensionOf(instruction, resource);
        var count = dimension == Gw2ShaderResourceDimension.Buffer ? 1 : CoordinateCount(dimension) + 1;
        var address = Source(instruction.Operands[1], Enumerable.Range(0, count).ToArray(), ValueType.Int);
        var arguments = new List<string> { address };
        if (instruction.SampleOffsets is { } offsets && (offsets.U != 0 || offsets.V != 0 || offsets.W != 0)) arguments.Add(OffsetText(offsets, count - 1));
        StoreResourceResult(instruction, instruction.Operands[0], resource, $"{TextureName(resource.FirstIndex)}.Load({string.Join(", ", arguments)})");
    }

    private void ResourceInfo(Gw2Sm5Instruction instruction)
    {
        var destination = instruction.Operands[0];
        var resource = instruction.Operands[2];
        var dimension = DimensionOf(instruction, resource);
        if (dimension is not (Gw2ShaderResourceDimension.Texture2D or Gw2ShaderResourceDimension.TextureCube)) throw new NotSupportedException($"resinfo on {dimension}");
        var returnsUint = (instruction.Controls & 3) == 2;
        var level = Source(instruction.Operands[1], [0], ValueType.Uint);
        var name = $"info{instruction.Offset}";
        Line($"uint4 {name} = 0;");
        Line($"{TextureName(resource.FirstIndex)}.GetDimensions({level}, {name}.x, {name}.y, {name}.w);");
        var components = DestinationComponents(destination);
        var selected = $"{name}.{string.Concat(components.Select(component => Components[resource.Swizzle[component]]))}";
        Store(instruction, destination, components, returnsUint ? selected : $"(float{WidthSuffix(components.Count)}){selected}", returnsUint ? ValueType.Uint : ValueType.Float);
    }

    private string DestinationName(Gw2Sm5Operand destination, IReadOnlyList<int> components)
    {
        if (destination.RegisterType == Gw2ShaderRegisterType.Temp) return TempDestinationText(_currentReader, destination.FirstIndex, components);
        var name = RegisterExpression(destination, out _);
        return destination.ComponentCount == 4 ? $"{name}.{string.Concat(components.Select(component => Components[component]))}" : name;
    }

    private string Source(Gw2Sm5Operand operand, IReadOnlyList<int> components, ValueType type, bool asCondition = false)
    {
        string expression;
        if (operand.RegisterType == Gw2ShaderRegisterType.Immediate32)
        {
            var values = components.Select(component => operand.Immediates[operand.ComponentCount == 4 ? operand.Swizzle[component] : 0]).ToArray();
            expression = ImmediateText(values, type);
            if (asCondition) expression = $"({expression} != 0u)";
            return ApplyModifier(expression, operand.Modifier, type);
        }

        string name;
        var registerType = operand.RegisterType == Gw2ShaderRegisterType.ImmediateConstantBuffer ? ValueType.Uint : ValueType.Float;
        if (operand.RegisterType == Gw2ShaderRegisterType.Temp && operand.ComponentCount == 4)
        {
            var registerComponents = components.Select(component => operand.Swizzle[component]).ToArray();
            name = TempSourceText(operand.FirstIndex, registerComponents);
            registerType = TempValueType(operand.FirstIndex, registerComponents);
        }
        else if (operand.RegisterType == Gw2ShaderRegisterType.ConstantBuffer && MatrixColumnSource(operand, components) is { } column)
            name = column;
        else if (WritesLiterally && LiteralInputSource(operand, components) is var (member, memberType))
            (name, registerType) = (member, memberType);
        else
        {
            name = RegisterExpression(operand, out _);
            if (operand.ComponentCount == 4) name += "." + string.Concat(components.Select(component => Components[operand.Swizzle[component]]));
        }
        if (registerType == ValueType.Bool && asCondition && operand.Modifier == Gw2Sm5OperandModifier.None)
            expression = name;
        else
        {
            expression = Convert(name, registerType, type);
            expression = ApplyModifier(expression, operand.Modifier, type);
            expression = asCondition ? $"({expression} != 0u)" : expression;
        }
        if (operand.RegisterType is Gw2ShaderRegisterType.Output or Gw2ShaderRegisterType.OutputDepth or Gw2ShaderRegisterType.IndexableTemp) MarkUntrackedRead();
        return operand.RegisterType == Gw2ShaderRegisterType.Temp && operand.ComponentCount == 4 ? TempReference(operand, components, type, asCondition, expression) : expression;
    }

    private static string ApplyModifier(string expression, Gw2Sm5OperandModifier modifier, ValueType type) => modifier switch
    {
        Gw2Sm5OperandModifier.Negate => $"-{expression}",
        Gw2Sm5OperandModifier.Absolute => $"abs({expression})",
        Gw2Sm5OperandModifier.AbsoluteNegate => $"-abs({expression})",
        _ => expression,
    };

    private string RegisterExpression(Gw2Sm5Operand operand, out ValueType registerType)
    {
        registerType = ValueType.Float;
        switch (operand.RegisterType)
        {
            case Gw2ShaderRegisterType.Temp: return TempSourceName(operand.FirstIndex, operand.Swizzle);
            case Gw2ShaderRegisterType.Input: return $"v{operand.FirstIndex}";
            case Gw2ShaderRegisterType.Output: return $"o{operand.FirstIndex}";
            case Gw2ShaderRegisterType.OutputDepth: return "oDepth";
            case Gw2ShaderRegisterType.IndexableTemp: return $"x{operand.FirstIndex}[{IndexText(operand.Indices[1])}]";
            case Gw2ShaderRegisterType.ImmediateConstantBuffer:
                registerType = ValueType.Uint;
                return $"icb[{IndexText(operand.Indices[0])}]";
            case Gw2ShaderRegisterType.ConstantBuffer: return ConstantExpression(operand);
            default: throw new NotSupportedException($"register type {operand.RegisterType}");
        }
    }

    private string ConstantExpression(Gw2Sm5Operand operand)
    {
        var slot = operand.FirstIndex;
        var index = operand.Indices[1];
        var register = (int)index.Immediate;
        if (slot == 0)
        {
            foreach (var uniform in ConstantUniforms())
            {
                var start = uniform.Register / ConstantRegisterBytes;
                var count = Math.Max(uniform.Count, 1);
                if (register < start || register >= start + count) continue;
                if (count == 1 && index.Relative is null) return UniformName(uniform);
                var offset = register - start;
                return $"{UniformName(uniform)}[{RelativeText(index.Relative, offset)}]";
            }
        }

        if (index.Relative is not null) throw new NotSupportedException($"relative index into unnamed cb{slot}[{register}]");
        return $"cb{slot}_c{register}";
    }

    private bool IsMatrix(Gw2ShaderReflectionEntry uniform) =>
        (uniform.Type & 0x0F) == MatrixUniformType && uniform.Count % MatrixColumns == 0 && !_matrixUniformsKeptAsRegisters.Contains(uniform.Name);

    private void ChooseMatrixUniforms()
    {
        var current = _currentReader;
        for (var index = 0; index < Instructions.Count; index++)
        {
            if (Gw2Sm5OpCodes.IsDeclaration(Instructions[index].OpCode)) continue;
            _currentReader = index;
            foreach (var operand in Instructions[index].Operands)
                if (operand is { RegisterType: Gw2ShaderRegisterType.ConstantBuffer, FirstIndex: 0 } && operand.Indices[1].Relative is { } relative &&
                    ConstantUniformAt((int)operand.Indices[1].Immediate) is { } uniform && IsMatrix(uniform) && MatrixIndexBeforeShift(relative) is null)
                    _matrixUniformsKeptAsRegisters.Add(uniform.Name);
        }

        _currentReader = current;
    }

    private Gw2ShaderReflectionEntry? ConstantUniformAt(int register) =>
        ConstantUniforms().FirstOrDefault(uniform => register >= uniform.Register / ConstantRegisterBytes && register < uniform.Register / ConstantRegisterBytes + Math.Max(uniform.Count, 1));

    private string? MatrixColumnSource(Gw2Sm5Operand operand, IReadOnlyList<int> components) =>
        MatrixRegister(operand) is var (matrix, column) ? $"{matrix}.{string.Concat(components.Select(component => $"_m{operand.Swizzle[component]}{column}"))}" : null;

    private (string Matrix, int Column)? MatrixRegister(Gw2Sm5Operand operand)
    {
        if (operand.RegisterType != Gw2ShaderRegisterType.ConstantBuffer || operand.FirstIndex != 0 || operand.ComponentCount != 4) return null;
        var index = operand.Indices[1];
        if (ConstantUniformAt((int)index.Immediate) is not { } uniform || !IsMatrix(uniform)) return null;
        var offset = (int)index.Immediate - uniform.Register / ConstantRegisterBytes;
        var column = offset % MatrixColumns;
        var element = offset / MatrixColumns;
        var matrix = UniformName(uniform);
        if (uniform.Count > MatrixColumns)
        {
            var elementText = element.ToString(CultureInfo.InvariantCulture);
            if (index.Relative is not null)
            {
                var matrixIndex = MatrixIndexBeforeShift(index.Relative)!;
                elementText = element == 0 ? matrixIndex : $"{matrixIndex} + {elementText}";
            }

            matrix += $"[{elementText}]";
        }

        return (matrix, column);
    }

    private string? MatrixIndexBeforeShift(Gw2Sm5Operand relative)
    {
        if (_currentReader < 0) return null;
        var reaching = _readDefinitions.ContainsKey(_currentReader) ? ReachingDefinition(_currentReader, relative, 0) : StraightLineDefinition(_currentReader, relative.FirstIndex, relative.Swizzle[0]);
        if (reaching is not { } shiftIndex) return null;
        var shift = Instructions[shiftIndex];
        var component = relative.Swizzle[0];
        if (shift.Name != "ishl" || shift.Operands[2] is not { RegisterType: Gw2ShaderRegisterType.Immediate32 } amount) return null;
        if (amount.Immediates[amount.ComponentCount == 4 ? amount.Swizzle[component] : 0] != 2) return null;
        if (!UnchangedBetween(shift.Operands[1], shiftIndex, _currentReader)) return null;
        return SourceAt(shiftIndex, shift.Operands[1], [component], ValueType.Int);
    }

    private int? StraightLineDefinition(int reader, int temp, int component)
    {
        for (var index = reader - 1; index >= 0; index--)
        {
            var instruction = Instructions[index];
            if (Gw2Sm5OpCodes.IsDeclaration(instruction.OpCode)) return null;
            if (ControlFlowNames.Contains(instruction.Name) || ConditionalControlFlowNames.Contains(instruction.Name)) return null;
            for (var destinationIndex = 0; destinationIndex < DestinationCount(instruction) && destinationIndex < instruction.Operands.Count; destinationIndex++)
                if (instruction.Operands[destinationIndex] is { RegisterType: Gw2ShaderRegisterType.Temp, ComponentCount: 4 } destination &&
                    destination.FirstIndex == temp && DestinationComponents(destination).Contains(component))
                    return index;
        }

        return null;
    }

    private string IndexText(Gw2Sm5Index index) => RelativeText(index.Relative, (int)index.Immediate);

    private string RelativeText(Gw2Sm5Operand? relative, int offset)
    {
        if (relative is null) return offset.ToString(CultureInfo.InvariantCulture);
        var expression = Source(relative, [0], ValueType.Int);
        return offset == 0 ? expression : $"{expression} + {offset}";
    }

    private string SelectText(string condition, string whenTrue, string whenFalse, int width) =>
        _target == ShaderCompilerTarget.Dxc && width > 1 ? $"select({condition}, {whenTrue}, {whenFalse})" : $"{condition} ? {whenTrue} : {whenFalse}";

    private string Convert(string expression, ValueType from, ValueType to)
    {
        if (from == to) return expression;
        if (from == ValueType.Bool) return Convert($"({SelectText(expression, "0xffffffffu", "0u", 4)})", ValueType.Uint, to);
        if (to == ValueType.Bool) return $"({Convert(expression, from, ValueType.Uint)} != 0u)";
        return to switch
        {
            ValueType.Float => $"asfloat({expression})",
            ValueType.Int => $"asint({expression})",
            _ => $"asuint({expression})",
        };
    }

    private static string ImmediateText(uint[] values, ValueType type)
    {
        var texts = values.Select(value => ImmediateValueText(value, type)).ToArray();
        if (texts.Length == 1) return texts[0];
        return $"{ValueTypeName(type)}{texts.Length}({string.Join(", ", texts)})";
    }

    private static string ImmediateValueText(uint value, ValueType type)
    {
        switch (type)
        {
            case ValueType.Int:
                return unchecked((int)value).ToString(CultureInfo.InvariantCulture);
            case ValueType.Uint:
                return value <= 0xFFFF ? $"{value}u" : $"0x{value:x8}u";
        }

        var number = BitConverter.UInt32BitsToSingle(value);
        if (!float.IsFinite(number) || (number != 0f && !float.IsNormal(number)) || (value == 0x80000000)) return $"asfloat(0x{value:x8}u)";
        var text = number.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.', StringComparison.Ordinal) && !text.Contains('E', StringComparison.Ordinal)) text += ".0";
        return text;
    }

    private static ValueType ReturnValueType(int returnType) => returnType switch
    {
        3 => ValueType.Int,
        4 => ValueType.Uint,
        _ => ValueType.Float,
    };

    private static string ReturnTypeName(int returnType) => ValueTypeName(ReturnValueType(returnType));

    private static string ValueTypeName(ValueType type) => type switch
    {
        ValueType.Int => "int",
        ValueType.Uint => "uint",
        ValueType.Bool => "bool",
        _ => "float",
    };

    private static string TextureTypeName(Gw2ShaderResourceDimension dimension) => dimension switch
    {
        Gw2ShaderResourceDimension.Buffer => "Buffer",
        Gw2ShaderResourceDimension.Texture1D => "Texture1D",
        Gw2ShaderResourceDimension.Texture2DMultisampled => "Texture2DMS",
        Gw2ShaderResourceDimension.Texture3D => "Texture3D",
        Gw2ShaderResourceDimension.TextureCube => "TextureCube",
        Gw2ShaderResourceDimension.Texture1DArray => "Texture1DArray",
        Gw2ShaderResourceDimension.Texture2DArray => "Texture2DArray",
        Gw2ShaderResourceDimension.Texture2DMultisampledArray => "Texture2DMSArray",
        Gw2ShaderResourceDimension.TextureCubeArray => "TextureCubeArray",
        _ => "Texture2D",
    };
}
