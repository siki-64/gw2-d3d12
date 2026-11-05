using Gw2.Client.Scene;
using Gw2.Client.Settings;
using Gw2.Client.Ui;
using Gw2.Formats;

namespace Gw2.Client;

public sealed partial class Gw2Client
{
    private const float MaximumRotationSpeedPercent = CameraPreferenceChunk.MinimumRotationSpeedPercent + byte.MaxValue * 0.5859375f;

    private static readonly UiCameraOptionLimits CameraOptionLimits = new(
        CameraPreferenceChunk.MaximumCollisionSensitivity,
        CameraPreferenceChunk.MaximumZoomSpeedOption,
        CameraPreferenceChunk.MinimumFieldOfViewDegrees,
        CameraPreferenceChunk.MaximumFieldOfViewDegrees,
        CameraPreferenceChunk.MinimumRotationSpeedPercent,
        MaximumRotationSpeedPercent);

    private byte[]? _cameraPreferencesSource;
    private CameraPreferences _cameraPreferences = CameraPreferences.Defaults;
    private CameraPreferences? _editedCameraPreferences;
    private Gw2GeneralState? _generalState;

    private CameraPreferences OwnCameraPreferences
    {
        get
        {
            if (_editedCameraPreferences is { } edited) return edited;
            var pack = _login?.AccountPreferencePack;
            if (!ReferenceEquals(pack, _cameraPreferencesSource))
            {
                _cameraPreferencesSource = pack;
                _cameraPreferences = pack is null ? CameraPreferences.Defaults : CameraPreferenceChunk.Read(pack);
            }
            return _cameraPreferences;
        }
    }

    private Gw2GeneralState GeneralState => _generalState ??= Gw2GeneralSettings.Load();

    private int OwnCameraSmoothingMode => GeneralState.CameraSmoothing ? Gw2WorldCameraMotion.CharacterSmoothing : Gw2WorldCameraMotion.NoSmoothing;

    private void UpdateGeneralOptionsWindow()
    {
        if (_perfOverlay?.GeneralOptions is not { } window) return;
        if (!window.IsOpen)
        {
            window.Camera = null;
            return;
        }
        window.Limits ??= CameraOptionLimits;
        window.Camera ??= CameraOptionsOf(OwnCameraPreferences, GeneralState);
        if (window.Edited is { } edited)
        {
            window.Edited = null;
            _editedCameraPreferences = OwnCameraPreferences with
            {
                SnapsCloseToTheCharacter = edited.SnapsCloseToTheCharacter,
                CollisionSensitivity = CameraPreferenceChunk.MaximumCollisionSensitivity - edited.CollisionSensitivity,
                ZoomSpeedOption = edited.ZoomSpeedOption,
                FieldOfViewDegrees = edited.FieldOfViewDegrees,
                RotationSpeedPercent = edited.RotationSpeedPercent,
            };
            if (edited.Smoothing != GeneralState.CameraSmoothing)
            {
                _generalState = GeneralState with { CameraSmoothing = edited.Smoothing };
                Gw2GeneralSettings.Save(_generalState);
            }
        }
        if (!window.EditFinished) return;
        window.EditFinished = false;
        if (_editedCameraPreferences is { } finished && _login?.AccountPreferencePack is { } pack && finished != CameraPreferenceChunk.Read(pack))
            _login.UploadAccountPreferences(CameraPreferenceChunk.ReplaceIn(pack, finished));
        _editedCameraPreferences = null;
    }

    private static UiCameraOptions CameraOptionsOf(CameraPreferences preferences, Gw2GeneralState general) => new(
        general.CameraSmoothing,
        preferences.SnapsCloseToTheCharacter,
        CameraPreferenceChunk.MaximumCollisionSensitivity - preferences.CollisionSensitivity,
        preferences.ZoomSpeedOption,
        preferences.FieldOfViewDegrees,
        preferences.RotationSpeedPercent);
}
