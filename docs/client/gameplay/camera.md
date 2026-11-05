# World camera: new client

How the new client implements [World camera](../../../gw2-research/docs/gameplay/camera.md). The retail behaviour and its evidence are in that note.

## Mouse look and zoom

`Gw2Client` uses these rates and limits while it follows the own character. It starts at the account's saved `cameraDistance`.

## Anchor

The camera looks at the anchor `max(40, 0.8 · height)` above the character, from `WvCamera_ShapeDistanceAndPitch`.
The zoom-dependent lift and the sideways and pitch offsets are not applied yet.

## General Options

The Escape menu opens General Options. These camera options come from the account's `uxac` chunk and apply as they change:
- snapping close to the character
- collision sensitivity
- zoom speed
- field of view
- rotation speed

When a slider is released, `CameraPreferenceChunk.ReplaceIn` writes them back and the client uploads the pack, as it does for keybinds, so the retail client sees them too. The other fields of the chunk go back unchanged.

The stored collision sensitivity is the number of probes that must be blocked before the eye pulls in, so 0 is the most sensitive. The slider shows `9 − stored` so that a higher value means more sensitive. Retail shows the stored value directly, so its slider runs backwards (a known retail UI bug); ours is deliberately different. Only the display differs: the account value is the same.

**Camera smoothing** is our own option; retail has none, and players use addons to remove it. It is stored in `GeneralSettings.xml` beside our graphics settings, never in the account. Turning it off uses the exe's smoothing mode 0 (`NoSmoothing`): the snap distance is 0, so the centre snaps to the anchor every frame instead of easing at 20 per second.

## Action camera and gamepad

The game's "Toggle Action Camera" (control 18) and "Disable Action Camera" (control 19) keybinds switch it.

While it is on:
- the mouse turns the camera without a held button;
- the centre follows the character directly;
- the camera direction sent in 0x0b is the flat facing.

The retail client has no gamepad input; this is our own addition, read through Crystal's `InputFrame.GamePad` (SDL, first pad):
- **Left stick:** analog forward/strafe with Crystal's dead zone. The held-key bits stay 0, as in the retail action camera capture.

## Mouse capture

While the left or right mouse button is held, or the action camera is on, the client switches Crystal's `Mouse.IsRelativeMode` on:
- the cursor is hidden and confined;
- look input comes from `InputFrame.RelativeMovement`, so dragging no longer stops at the screen edge.

When the capture ends, the cursor goes back to where the drag started. The capture is also released when the window loses focus or ImGui owns the mouse. Hosted windows can't capture; there the client falls back to absolute cursor movement.
