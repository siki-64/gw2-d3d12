# Commits
- Commit on work branches after each completed milestone.

# Pull Requests
- Humans open PRs manually.
- Agent writes title + short summary (max 100 words).

# Code Style
- No comments. Documentation lives outside of code.
- Names for code and data must be self-explanatory.

# Workflow
- Decompile only the routines a feature needs; this is not a full source decompilation.

1. Find the behaviour in IDA and write it down in `gw2-research/docs/`.
2. Decode it in the dat or protocol packages, with a test against a real file or capture.
3. Use it in the client in the same shape and at the same point as the exe does.
4. Compare against the live client (MumbleLink positions, screenshots, x64dbg) and fix the decode side, not the presentation.

# Original Assets
- Never commit original assets from the .exe or .dat, including shader bytecode.
- Shaders the client ships are high-level source (HLSL/.fx) that the build compiles.
