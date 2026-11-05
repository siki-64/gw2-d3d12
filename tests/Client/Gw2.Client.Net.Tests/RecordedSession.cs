using Gw2.Formats;
using System;
using System.IO;
using Gw2.Protocol.Capture;
using Gw2.Protocol.Schema;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

internal static class RecordedSession
{
    private const string CaptureVariable = "GW2_NETWORK_CAPTURE";
    private static ExecutableProtocolSchemas? _schemas;
    private static HandshakeGroup? _group;

    public static ExecutableProtocolSchemas Schemas => _schemas ??= ExecutableProtocolSchemas.FromExecutable(ExecutablePath());

    public static HandshakeGroup Group => _group ??= HandshakeGroup.FromExecutable(ExecutablePath());

    public static NetworkCapture Capture()
    {
        var path = Environment.GetEnvironmentVariable(CaptureVariable);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Inconclusive($"Set {CaptureVariable}.");
        var capture = NetworkCapture.Read(path);
        if (capture.Build != Schemas.Build) Assert.Inconclusive($"capture is build {capture.Build}, the installed exe is build {Schemas.Build}");
        return capture;
    }

    private static string ExecutablePath()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null) Assert.Inconclusive("Gw2-64.exe is not installed.");
        return path;
    }
}
