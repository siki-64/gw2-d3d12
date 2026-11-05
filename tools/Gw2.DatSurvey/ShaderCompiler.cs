using System.Runtime.InteropServices;
using System.Text;
using Gw2.Dat.Content;

namespace Gw2.DatSurvey;

public sealed record ShaderCompileResult(byte[]? Code, string? Errors);

public static class ShaderCompiler
{
    public const uint OptimizationLevel3 = 1u << 15;
    public const uint SkipOptimization = 1u << 2;
    private const int SkipOptimizationGlobalFlag = 1 << 4;

    public static uint FlagsMatching(ReadOnlyMemory<byte> originalShex) =>
        Gw2Sm5Program.TryParse(originalShex)?.Instructions.Any(instruction => instruction.Name == "dcl_globalFlags" && (instruction.Controls & SkipOptimizationGlobalFlag) != 0) == true ? SkipOptimization : 0;

    public static string? CompileErrors(string source, string target) => Compile(source, target).Errors;

    public static ShaderCompileResult Compile(string source, string target, uint flags = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("D3DCompile needs Windows (d3dcompiler_47.dll)");
        var bytes = Encoding.UTF8.GetBytes(source);
        var result = D3DCompile(bytes, (UIntPtr)(ulong)bytes.Length, "decompiled.hlsl", IntPtr.Zero, IntPtr.Zero, "main", target, flags, 0, out var code, out var errors);
        try
        {
            if (result != 0) return new(null, BlobText(errors) ?? $"D3DCompile failed (0x{result:X8})");
            return new(BlobBytes(code), null);
        }
        finally
        {
            if (code != IntPtr.Zero) Marshal.Release(code);
            if (errors != IntPtr.Zero) Marshal.Release(errors);
        }
    }

    private static string? BlobText(IntPtr blob) => blob == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(BlobPointer(blob));

    private static byte[] BlobBytes(IntPtr blob)
    {
        var vtable = Marshal.ReadIntPtr(blob);
        var getSize = Marshal.GetDelegateForFunctionPointer<GetBufferSize>(Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size));
        var bytes = new byte[(int)getSize(blob)];
        Marshal.Copy(BlobPointer(blob), bytes, 0, bytes.Length);
        return bytes;
    }

    private static IntPtr BlobPointer(IntPtr blob)
    {
        var vtable = Marshal.ReadIntPtr(blob);
        var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferPointer>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
        return getBuffer(blob);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetBufferPointer(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate UIntPtr GetBufferSize(IntPtr self);

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int D3DCompile(byte[] source, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string sourceName, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entryPoint, [MarshalAs(UnmanagedType.LPStr)] string target, uint flags, uint effectFlags, out IntPtr code, out IntPtr errors);
}
