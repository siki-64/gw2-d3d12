# Debug UI (Dear ImGui): new client

How the new client implements [Debug UI (Dear ImGui)](../../../gw2-research/docs/engine/debug-ui.md). The retail behaviour and its evidence are in that note.

#### Client

`Gw2.Client.Ui` draws the same window with the engine's `Crystal.ImGui` (Dear ImGui 1.93; window `UiPerfGraphsWindow`, renderer `ImGuiRenderer`). Shift+F10 toggles it and `--perf-graphs` opens it at start. Both are client-only: the exe opens the window through its Gr debug object factory. Next to it, the client-only `UiCollisionWindow` shows the collision ground under the character (a ray from 50 units above the feet down to 5000 below) with its body kind, placement index and normal; startup also writes it as a `COLLISION` line.

- Same as the exe: window size 650×600, Locked/Paused/Transparent with the exe tooltips, one collapsing header per graph, one PlotLines per series over a 250-sample ring with the `%.3f` overlay, the plot width rule and the single open graph.
- Frame Time (ms) is sampled once per present with the GrPresent smoothing (`Gw2FrameTimeSmoothing`).
- Client-only graphs:
  - CPU and GPU (ms): the frame's CPU work and the fork `FrameProfiler` GPU time.
  - GPU sections (ms): one series per `GrFrameSection`.
  - CPU sections (ms): the CPU time between the same marks; the first series (Update) runs from the first update of the frame to the start of draw.
- Left out:
  - Start/Stop Tracking and the Transient Buffer Tracking File, because the client has no transient buffers.
  - The server graphs (CPU utilisation, network traffic, ping), until the world connection feeds them.
- The camera ignores the mouse while ImGui wants it, like GrUi_WantsMouse.

## Debug modes

In the remake, `Gw2.Client --debug-view <name>` selects the view at start, and Shift+F9 opens the client-only ImGui
window `UiDebugViewWindow` that switches it at runtime (`GrDebugView`). Its "Collider capsules" box draws every agent's collision capsule, the own character included; it is off by default. Its "Debug HUD panels" box shows the ImGui panels for the own vitals, the target and the specializations; also off by default. The views:
