# Agent command queue (build 208784): new client

How the new client implements [Agent command queue (build 208784)](../../../gw2-research/docs/engine/agent-command-queue.md). The retail behaviour and its evidence are in that note.

## Remake

`AgContext.QueueCommand`, `AgContext.Tick`, `AgChar.AcceptsPredictedCommand`, `AgChar.RunCommand` and `AgChar.FinishCommand` follow these routines for `AgCommandControl`, the only command queued so far. Not modelled yet: the agent pending list link (slot 48) and the agent registration, the active-layer branches (0x1800/0x1000; control commands never set them), the per-agent tick and the mode 0 species check in `AgCommand_Fire`, and a caller for the agent world tick (the exe calls it through the world vtable from the frame loop).
