# Map load screen (`GameLoad.cpp`): new client

How the new client implements [Map load screen (`GameLoad.cpp`)](../../../gw2-research/docs/ui/load-screen.md). The retail behaviour and its evidence are in that note.

## In the client

`UiLoginScreen.ShowLoadScreen` draws the image filling the window from Play until the map is loaded, with the login
status at the bottom. The map name, the progress bar and the conditional images are not drawn yet.

## Load-in order

Following [map-load.md](../../../gw2-research/docs/protocol/map-load.md):

1. `GameSession` reads the spawn position from `0x424` (field 1, game units). The client places the character there
   before it reads the map, so terrain and props stream around the spawn point.
2. Once streaming settles, the client reports the map loaded, but keeps the load screen up.
3. The load screen closes once the controlled agent exists, as the exe's loader waits in `STATE_AGENT_STREAM`.
   Until then the progress text shows `Position  waiting for server`.

The `0x424` heading is not used yet.
