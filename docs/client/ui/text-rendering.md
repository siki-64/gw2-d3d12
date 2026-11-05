# Text rendering: new client

How the new client implements [Text rendering](../../../gw2-research/docs/ui/text-rendering.md). The retail behaviour and its evidence are in that note.

## Nameplate depth (remake)

- The official client draws nameplates as UI with no depth. The remake depth-tests them per pixel. Every agent on screen gets an anchor (projected head position and depth), named or not, at any distance; the label range applies only to the remake's own labels. A text frame whose text matches an anchor name takes that depth; an unmatched text frame takes the nearest unnamed anchor within 60 px; other frames up to 300 px wide (tags, health bars) take the depth of the nearest matched text frame within 60 px.
