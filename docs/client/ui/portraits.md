# Portrait render targets (build 209026): new client

How the new client implements [Portrait render targets (build 209026)](../../../gw2-research/docs/ui/portraits.md). The retail behaviour and its evidence are in that note.

## Camera and lights (`sub_1402F7190`, after the pose)

`Gw2PortraitCamera` (`Gw2.Client.Scene`) holds this camera and the enabled lights.
