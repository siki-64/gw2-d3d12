# Compass map image: new client

How the new client implements [Compass map image](../../../gw2-research/docs/ui/compass-map.md). The retail behaviour and its evidence are in that note.

## Pages per zoom level

The remake draws the background, then terrain and overlay of that level into its own 512×512 target, and gives that
target to the compass submodel.

## Exploration texture (`sub_1414D1D50`, `WorldMapManager.cpp`)

The remake reads this buffer (`CompassExploration`) and samples it at the continent position of each quad corner.

## Player arrow (`WmmAgent.cpp`)

The remake takes the facing from `CoChar`.
