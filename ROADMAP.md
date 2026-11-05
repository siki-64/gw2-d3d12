# Open work

Open tasks only. Finished work and findings go to the note under `gw2-research/docs/` for their subject; remove the task here and reorder accordingly.
Each numbered step is sized to end in one PR.

Every check below compares a capture of the replacement against a vanilla capture of the same scenario
(`Gw2.Protocol.Capture`, `RecordedSessionTests`). A divergence names the connection, message and byte.

## Protocol parity

1. **Sender trigger table.** For each of the 509 registered send ids (`tools/ida/export_sender_triggers.py`), record what triggers the
   send (input, timer, inbound message, simulation tick such as the implicit `0x11` in `AgWorld_QueueCommand`) and
   where each field value comes from. Names are matched across builds by schema shape plus code signature. Done when
   every id has a trigger and value sources, or is marked unreached.
2. **Deterministic inputs.** The inputs are inventoried and recorded
   ([time-and-entropy.md](gw2-research/docs/protocol/time-and-entropy.md)): entropy rebuilds every hello, the event tick
   clocks fix the flush points, and the only random value on the wire is a fixed per-thread sequence. Left: a replay
   source that feeds recorded clocks and entropy to the client's frame loop, and whether the portal SRP exchange needs its
   `CryptGenRandom` output recorded. Done when a vanilla capture replayed against itself reproduces the handshake and
   first frames.

## Client foundation

3. **Input, movement and camera.** The player side that produces outbound traffic: input mapping, the exe's movement
   and collision response, jump and dodge, the camera controller, targeting and skill activation, ported from the
   routines that feed the senders in the trigger table. Done when walking, turning and jumping send what the exe
   sends for the same input.

   Progress: the world camera is documented in `gw2-research/docs/gameplay/camera.md`. That covers the follow update
   (snap distances, ray cast clearance, easing), the default placement, the anchor transition, the follow modes and
   the settings springs (field of view 50°, zoom limits 100–600, starting zoom 350) and the tweaker defaults.
   `Gw2WorldCameraMotion` in the client ports the angle wrapping, the shorter-arc yaw turn, the easing, the
   critically damped spring and the placement. Input: the engine has `InputFrame`, `InputMap<TAction>` and relative mouse mode, and the
   client reads input through them; the exe's `InputBinding` module is located (`gw2-research/docs/gameplay/input-binding.md`).
   Walking (`0x0b`, `0x04`, `0x12`), jumping (`0x09`) and dodging (`6C00xx`, then `0x1a`) and landing (`0x1a` one step later) send what the recordings hold. Tab selects locally and sends nothing; a skill used on the target sends `0xe1` once per new target and a `2B` naming it before the `23`. Weapon swap sends `0A` and dodging waits for endurance (`0xd9`/`0xdb`). The other `0x17` skill payloads are documented in `gw2-research/docs/protocol/skill-commands.md`, and `OwnCharacterMovement` builds the skill press (`2A`), use (`23`) and release (`29`) byte for byte. `Gw2SkillPalettes` resolves the default weapon bar from the content palettes. Keys 1–5 send them for the default bar of the active weapons, read from `0xb6`, `0x3cb`, `0x121` and `0x12b`. Open: skills that are on no default bar (41253 in capture 000717), ranger pet and thief stolen skills. The engineer toolbelt resolves from the bar skills' abilities. The other professions resolve F1–F5 from their mechanic palette configs, elites included. Keys 6–0 send slots 0–4 from `0x264` palettes, the `0x1F1` legend and the `0x282` specializations; F1–F5 send the revenant legend swap and legend skills from the legend palette configs, the `0x1ED`/`0x1EF` legends, the buffs (`0x2DF`/`0x2E2`/`0x2E3`) and the elite minors.
   Mouse look and zoom use the exe's rates and limits, and the distance eases toward the zoom at the follow rate 1.5. `0x13` (a `0x12` with an extra float) shows up only around fast skill or mount movement and belongs with step 4. The camera centre follows the own character with the follow update's snap and easing. Open: the follow update's obstruction pull-in and ray casts. Right-drag turns the walk direction with the camera; left-drag orbits only. Holding a ground skill key sends the reticle `2B`/`2C` cadence of the recording.

## Scenarios

Login, character select, map entry and idle, walking, jumping and combat are done. Each scenario is done when the replacement's capture compares clean against a vanilla capture of the same steps.

4. **Mounts.** Mounting, mounted movement and dismounting, with the movement sends at the exe's rates and quantisation.
5. **UI.** The client's own login, character select and game UI, then chat, inventory, trading post and the other
    UI-driven sends.

    Progress: the game HUD draws the skill bar, profession mechanic panel and the skill cast bar (warmup, channel and
    charge segments, stopped by the order's `0x45` event) through the exe's UI quads. Open: login and character select
    in the exe's UI, chat, inventory and the other windows.

## World state from the stream

6. **Inbound handlers.** Rebuild each inbound handler from its message fields to the state it changes
   ([handler-to-subsystem.md](gw2-research/docs/protocol/handler-to-subsystem.md)), in the shape of the `0x2E`
   `AgentMoveHandler`, with a test against a capture. Order by what the scenarios above need. Done per scenario
   when the replacement's world state agrees with the vanilla client's at the same capture point.

## Presentation

The decoders for models, animation, effects and maps stay. Their input changes from the exe's live objects to the
replacement's own world state. Anything documented only as a live read (the `Gw2.Exe.Live` snapshot readers) needs
the computation behind it found first.

7. **Characters.** Build characters, gear and dyes from the world state through the composite path
    (`CharacterScene`, `Gw2ModelBuilder`), with animation selection, sampling, blending and bone post-processing as
    documented under `gw2-research/docs/models/`. Done when players and NPCs match the live client in a side-by-side
    screenshot through idle, walk, run and one skill.

    Progress: players are built from the world state with their own face, hair, skin and hair dyes, equipped armour
    with per-player dyes and hidden slots, and the exe's default underwear. Open: the side-by-side screenshot check.
8. **Effects.** Spawn effects from the inbound messages that start them and play them with the existing decode
    ([effects.md](gw2-research/docs/effects/effects.md)). Done when an auto-attack, one ground-target and one projectile
    skill look like live.
9. **Picking.** Port the exe's CPU picking ([picking.md](gw2-research/docs/gameplay/picking.md)) against the
    replacement's world state, feeding the target sends of the combat scenario. `OwnAgentPicking` scores hover candidates and `OwnClickSelection` turns a left click into `0xeb`/`0xe3`. Open: the model point (token 51873) instead of the agent position, the character view height widening, the gadget prop raycast, the cursor-hit agent, Tab through the exe's path, and a live click recording.
