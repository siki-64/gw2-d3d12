using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Crystal.ShaderCompiler;
using Gw2.Dat.Content;
using Gw2.Dat.Shaders;

namespace Gw2.Client.Graphics;

public sealed record GrCompiledMaterialStage(byte[] Bytecode, IReadOnlyList<ShaderDescriptorSlot> DescriptorSlots);

public sealed class GrMaterialShaderCache : IDisposable
{
    private const int FileFormatVersion = 1;
    private static readonly byte[] FailedMarker = [0];
    private readonly string _directory;
    private RuntimeShaderCompiler? _compiler;

    public GrMaterialShaderCache(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        _directory = Path.Combine(rootDirectory, $"{FileFormatVersion}-{RuntimeShaderCompiler.Version}");
        Directory.CreateDirectory(_directory);
    }

    public static string DefaultRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gw2ClientRemake", "MaterialShaders");

    public int CompiledCount { get; private set; }
    public int LoadedCount { get; private set; }
    public int FailedCount { get; private set; }

    public GrCompiledMaterialStage? Stage(Gw2BgfxShaderBinary shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        var path = Path.Combine(_directory, $"{shader.CodeHash().ToString("X8", CultureInfo.InvariantCulture)}.{(shader.Stage == Gw2BgfxShaderStage.Vertex ? "vs" : "ps")}");
        if (File.Exists(path))
        {
            var cached = Read(path);
            if (cached is null) FailedCount++;
            else LoadedCount++;
            return cached;
        }

        var compiled = Compile(shader);
        WriteAtomically(path, compiled);
        if (compiled is null) FailedCount++;
        else CompiledCount++;
        return compiled;
    }

    private GrCompiledMaterialStage? Compile(Gw2BgfxShaderBinary shader)
    {
        string source;
        try
        {
            source = ShaderDecompiler.Decompile(shader, ShaderCompilerTarget.Dxc);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }

        _compiler ??= new RuntimeShaderCompiler(columnMajorMatrices: true);
        var stage = shader.Stage == Gw2BgfxShaderStage.Vertex ? ShaderStage.Vertex : ShaderStage.Pixel;
        var compilation = _compiler.CompileStage("material.hlsl", source, "main", stage);
        return compilation.Bytecode is { } bytecode ? new GrCompiledMaterialStage(bytecode, compilation.DescriptorSlots) : null;
    }

    private static GrCompiledMaterialStage? Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.BaseStream.Length <= FailedMarker.Length) return null;
        var slots = new ShaderDescriptorSlot[reader.ReadInt32()];
        for (var index = 0; index < slots.Length; index++)
            slots[index] = new ShaderDescriptorSlot(reader.ReadString(), reader.ReadChar(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        var bytecode = reader.ReadBytes(reader.ReadInt32());
        return new GrCompiledMaterialStage(bytecode, slots);
    }

    private static void WriteAtomically(string path, GrCompiledMaterialStage? stage)
    {
        var temporaryPath = path + "." + Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporaryPath)))
        {
            if (stage is null) writer.Write(FailedMarker);
            else
            {
                writer.Write(stage.DescriptorSlots.Count);
                foreach (var slot in stage.DescriptorSlots)
                {
                    writer.Write(slot.Name);
                    writer.Write(slot.RegisterClass);
                    writer.Write(slot.Register);
                    writer.Write(slot.Space);
                    writer.Write(slot.Slot);
                }
                writer.Write(stage.Bytecode.Length);
                writer.Write(stage.Bytecode);
            }
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void Dispose()
    {
        _compiler?.Dispose();
        _compiler = null;
    }
}
