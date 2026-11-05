# Docs

Notes on the new client under `src/Client/`: how it is built and where it differs from the retail client. The retail
client's notes are in [`gw2-research/docs/`](../gw2-research/docs/README.md), by subject. A note there holds the
`.dat` and executable evidence for its subject, and this tree links to it instead of restating it. A sentence about
how the new client implements something belongs here, not in the retail note.

| Note | Title |
| --- | --- |
| [asset-pipeline.md](client/asset-pipeline.md) | Archive decode stages |
| [name-labels.md](client/name-labels.md) | Name labels until the client has its own HUD |
| [benchmarks.md](client/benchmarks.md) | Frame-rate benchmarks against live |
| [dat-conversion.md](client/dat-conversion.md) | Converting `src/Dat` into layouts and routines |
| [workflow.md](client/workflow.md) | Contract-to-client workflow |
| [volumetric-fog.md](client/volumetric-fog.md) | Volumetric fog: shadowed fog over the exe fog |
| [physical-bloom.md](client/physical-bloom.md) | Physical bloom: multi-mip bloom over the exe chain |
| [hidden-global-water.md](client/hidden-global-water.md) | Hidden global water: the terrain-cell gate |

By subject, mirroring the retail note each one links back to:

| Note | Title |
| --- | --- |
| [archive/split-archive.md](client/archive/split-archive.md) | Split archive store: new client |
| [audio/map-audio.md](client/audio/map-audio.md) | Map audio: new client |
| [content/cntc-pack-cache.md](client/content/cntc-pack-cache.md) | Content pack cache: new client |
| [effects/aoe-telegraphs.md](client/effects/aoe-telegraphs.md) | AoE telegraphs: new client |
| [effects/effects.md](client/effects/effects.md) | Effects: new client |
| [effects/particles.md](client/effects/particles.md) | Particles: new client |
| [engine/agent-command-queue.md](client/engine/agent-command-queue.md) | Agent command queue (build 208784): new client |
| [engine/bgfx.md](client/engine/bgfx.md) | bgfx: new client |
| [engine/debug-ui.md](client/engine/debug-ui.md) | Debug UI (Dear ImGui): new client |
| [engine/havok.md](client/engine/havok.md) | Havok: new client |
| [engine/layout-mirrors.md](client/engine/layout-mirrors.md) | Layout mirrors: new client |
| [engine/pe-embedded-assets.md](client/engine/pe-embedded-assets.md) | Assets embedded in the PE: new client |
| [engine/shader-bytecode.md](client/engine/shader-bytecode.md) | Shader bytecode: new client |
| [engine/value-tables.md](client/engine/value-tables.md) | Value tables: new client |
| [gameplay/boons-conditions.md](client/gameplay/boons-conditions.md) | Effects: boons and conditions: new client |
| [gameplay/camera.md](client/gameplay/camera.md) | World camera: new client |
| [gameplay/dodge.md](client/gameplay/dodge.md) | Dodge: new client |
| [gameplay/skills.md](client/gameplay/skills.md) | Skills: new client |
| [maps/star-field.md](client/maps/star-field.md) | Star field: new client |
| [maps/zone-scatter.md](client/maps/zone-scatter.md) | Zone scatter (vegetation and ground cover): new client |
| [models/animation-controls.md](client/models/animation-controls.md) | Animation control objects (build 209026): new client |
| [models/bone-constraints.md](client/models/bone-constraints.md) | Bone constraints (build 209026): new client |
| [models/composite.md](client/models/composite.md) | Character composite: new client |
| [models/material-animation.md](client/models/material-animation.md) | Material animation: new client |
| [models/model-content.md](client/models/model-content.md) | Model content layout and dependency chain: new client |
| [models/model-pose-buffers.md](client/models/model-pose-buffers.md) | Model pose buffers (build 209026): new client |
| [protocol/agent-lifecycle.md](client/protocol/agent-lifecycle.md) | Agent lifecycle (AgWorld spawn/despawn) — scope: new client |
| [protocol/content-key-delivery.md](client/protocol/content-key-delivery.md) | Content key delivery: new client |
| [protocol/handler-to-subsystem.md](client/protocol/handler-to-subsystem.md) | Handler-to-subsystem traces: new client |
| [protocol/login-channel.md](client/protocol/login-channel.md) | Login channel: new client |
| [protocol/own-character-state.md](client/protocol/own-character-state.md) | Own character state (`0x12`): new client |
| [protocol/portal.md](client/protocol/portal.md) | Portal: new client |
| [protocol/skill-commands.md](client/protocol/skill-commands.md) | Skill and target commands (`0x17`): new client |
| [protocol/world-entry-live.md](client/protocol/world-entry-live.md) | World entry: live server: new client |
| [rendering/ambient-occlusion.md](client/rendering/ambient-occlusion.md) | Ambient occlusion: new client |
| [rendering/cube-maps.md](client/rendering/cube-maps.md) | Cube maps: new client |
| [rendering/culling.md](client/rendering/culling.md) | Culling: new client |
| [rendering/environment-lighting.md](client/rendering/environment-lighting.md) | Environment lighting (sun and ambient): new client |
| [rendering/particle-fields.md](client/rendering/particle-fields.md) | Environment particle fields: new client |
| [rendering/post-processing.md](client/rendering/post-processing.md) | Post-processing, HDR decode and screen/depth fades: new client |
| [rendering/shadows.md](client/rendering/shadows.md) | Variance shadow mapping (global `t15`): new client |
| [rendering/texture-quality.md](client/rendering/texture-quality.md) | Texture quality and residency: new client |
| [rendering/water.md](client/rendering/water.md) | Water: new client |
| [ui/compass-map.md](client/ui/compass-map.md) | Compass map image: new client |
| [ui/hero-paper-doll.md](client/ui/hero-paper-doll.md) | Hero panel paper doll (build 209026): new client |
| [ui/hud-art.md](client/ui/hud-art.md) | HUD art: new client |
| [ui/load-screen.md](client/ui/load-screen.md) | Map load screen (`GameLoad.cpp`): new client |
| [ui/nameplates.md](client/ui/nameplates.md) | Nameplates (infobars): new client |
| [ui/portraits.md](client/ui/portraits.md) | Portrait render targets (build 209026): new client |
| [ui/skill-bar.md](client/ui/skill-bar.md) | Skill bar: new client |
| [ui/text-rendering.md](client/ui/text-rendering.md) | Text rendering: new client |
