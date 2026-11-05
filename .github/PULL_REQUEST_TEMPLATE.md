<!--
Title: what changed, short. Summary: at most 100 words. No AI session links.
Tick what applies and strike or delete lines that don't.
-->

### Summary


### Workflow

- [ ] Behaviour found in IDA and written down in `gw2-research/docs/` (build and `sub_` addresses)
- [ ] Decoded in the dat or protocol packages, with a test against a real file or capture
- [ ] The client uses it in the same shape and at the same point as the exe
- [ ] Compared against the live client (MumbleLink, screenshots, x64dbg); differences fixed on the decode side

### Testing

- [ ] Build passes and the tests for the touched area pass
- [ ] Client render checked at a reference pose (screenshots below)

### Rules

- [ ] No comments in code; names are self-explanatory
- [ ] No original assets from the .exe or .dat, including shader bytecode
- [ ] Game-agnostic backend or platform code went to Crystal instead, merged there first
- [ ] `ROADMAP.md` and `gw2-research/docs/` are updated for what this closes
