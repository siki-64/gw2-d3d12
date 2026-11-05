# World entry: live server: new client

How the new client implements [World entry: live server](../../../gw2-research/docs/protocol/world-entry-live.md). The retail behaviour and its evidence are in that note.

## Client sends after the join

Live sends no own `0x39`: the remake's player is placed from `MapPosition` and sends nothing until its position is set
(it read -28, 114, 343 against the server's -28.5, 115.4, 343.7 m).
