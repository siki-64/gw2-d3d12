# Dodge: new client

How the new client implements [Dodge](../../../gw2-research/docs/gameplay/dodge.md). The retail behaviour and its evidence are in that note.

## Client reimplementation

- `Gw2ContentFunctions` decodes bindings and programs. The layout comes from the exe (`ContentFunctionContentLayout`).
- `OwnContentFunctionState` tracks `0x027e`/`0x027f` bindings and `0x018d`/`0x018e` movement modifiers.
- `Gw2ContentFunctionRunner` keeps a frame stack per instance and steps it every client tick. Wait, WaitForCondition, Conditional and RunFunctionStack block or branch. All other opcodes advance.
- Dodge input with a bound function sends `0x10c`, runs the first tick, then sends `0x10b`. It replaces the plain dodge.
- Conditional results go out as `0x10d`.
- LOCALE evaluates false. The instance counter is the client's own, starting at 1.
- `ContentFunctionCaptureTests` replays captures 133311 (Mirage) and 133600 (Vindicator) and checks the sequences match.
