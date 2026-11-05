using System;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.Versioning;

namespace Gw2.Client;

[SupportedOSPlatform("windows")]
public sealed class Gw2MumbleLink : IDisposable
{
    private const string MappingName = "MumbleLink";
    private const int MappingBytes = 0x1554;
    private const long ReopenIntervalMilliseconds = 5000;
    private const float InchesPerMetre = 39.37f;
    private const uint LinkVersion = 2;
    private const string LinkName = "Guild Wars 2";
    private const int VersionOffset = 0;
    private const int TickOffset = 4;
    private const int AvatarPositionOffset = 8;
    private const int AvatarFrontOffset = 20;
    private const int NameOffset = 44;
    private const int CameraPositionOffset = 556;
    private const int CameraFrontOffset = 568;
    private const int ContextLengthOffset = 1104;
    private const int ContextLength = 48;
    private const int MapIdOffset = 1136;

    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private long? _lastOpenAttempt;
    private uint _tick;

    public void Write(Vector3 avatarPosition, Vector2 avatarFacing, Vector3 cameraEye, Vector3 cameraTarget, uint mapId)
    {
        if (!TryOpen()) return;
        var view = _view!;
        view.Write(VersionOffset, LinkVersion);
        view.Write(TickOffset, ++_tick);
        WriteVector(view, AvatarPositionOffset, MetresOf(avatarPosition));
        WriteVector(view, AvatarFrontOffset, NormalizedOrZero(new Vector3(avatarFacing.X, 0f, avatarFacing.Y)));
        for (var index = 0; index < LinkName.Length; index++) view.Write(NameOffset + index * 2, LinkName[index]);
        view.Write(NameOffset + LinkName.Length * 2, '\0');
        WriteVector(view, CameraPositionOffset, MetresOf(cameraEye));
        WriteVector(view, CameraFrontOffset, NormalizedOrZero(LinkAxesOf(cameraTarget - cameraEye)));
        view.Write(ContextLengthOffset, ContextLength);
        view.Write(MapIdOffset, mapId);
    }

    private bool TryOpen()
    {
        if (_view is not null) return true;
        var now = Environment.TickCount64;
        if (now - _lastOpenAttempt < ReopenIntervalMilliseconds) return false;
        _lastOpenAttempt = now;
        try
        {
            _mapping = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.ReadWrite);
            _view = _mapping.CreateViewAccessor(0, MappingBytes);
            return true;
        }
        catch (System.IO.FileNotFoundException)
        {
            return false;
        }
    }

    private static Vector3 LinkAxesOf(Vector3 game) => new(game.X, -game.Z, game.Y);

    private static Vector3 MetresOf(Vector3 game) => LinkAxesOf(game) / InchesPerMetre;

    private static Vector3 NormalizedOrZero(Vector3 vector) =>
        vector.LengthSquared() > 1e-12f ? Vector3.Normalize(vector) : Vector3.Zero;

    private static void WriteVector(MemoryMappedViewAccessor view, int offset, Vector3 vector)
    {
        view.Write(offset, vector.X);
        view.Write(offset + 4, vector.Y);
        view.Write(offset + 8, vector.Z);
    }

    public void Dispose()
    {
        _view?.Dispose();
        _mapping?.Dispose();
    }
}
