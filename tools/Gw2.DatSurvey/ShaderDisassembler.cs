using System.Runtime.InteropServices;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public sealed record ShaderListing(int ShaderIndex, string Stage, string Text);

public static class ShaderDisassembler
{
    public static IReadOnlyList<ShaderListing> Disassemble(IGw2ContentSource source, uint materialFileId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("shader disassembly needs Windows (d3dcompiler_47.dll)");

        return Disassemble(source.DecodeAsset(materialFileId), $"File {materialFileId}");
    }

    public static IReadOnlyList<ShaderListing> Disassemble(Gw2AssetDocument document, string materialName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("shader disassembly needs Windows (d3dcompiler_47.dll)");

        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(document)
            ?? throw new InvalidDataException($"{materialName} is not a material");
        var listings = new List<ShaderListing>();
        for (var index = 0; index < material.Shaders.Count; index++)
        {
            var shader = material.Shaders[index];
            foreach (var binary in shader.Binaries)
            {
                if (binary.Backend != Gw2AmatShaderBackend.Dx11 || binary.Data.Length == 0) continue;
                var bgfxBinary = Gw2BgfxShaderBinary.TryParse(binary.Data);
                var container = bgfxBinary?.ChecksummedCode();
                if (container is null) continue;

                listings.Add(new(index, shader.IsPixelShader ? "ps" : "vs", UniformTable(bgfxBinary!) + Disassemble(container)));
                break;
            }
        }

        return listings;
    }

    private static string UniformTable(Gw2BgfxShaderBinary binary) =>
        string.Concat(binary.Uniforms.Select(uniform => $"// uniform {uniform.Name} {uniform.Kind} register {uniform.Register} count {uniform.Count}\n"));

    private static string Disassemble(byte[] container)
    {
        var source = Marshal.AllocHGlobal(container.Length);
        try
        {
            Marshal.Copy(container, 0, source, container.Length);
            var result = D3DDisassemble(source, (UIntPtr)(ulong)container.Length, 0, IntPtr.Zero, out var blob);
            if (result != 0) throw new InvalidOperationException($"D3DDisassemble failed (0x{result:X8})");

            try
            {
                var vtable = Marshal.ReadIntPtr(blob);
                var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferPointer>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
                return Marshal.PtrToStringAnsi(getBuffer(blob)) ?? string.Empty;
            }
            finally
            {
                Marshal.Release(blob);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetBufferPointer(IntPtr self);

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int D3DDisassemble(IntPtr source, UIntPtr size, uint flags, IntPtr comments, out IntPtr disassembly);
}
