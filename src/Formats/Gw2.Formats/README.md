# Gw2.Formats

Routines of the original client that everything else stands on and that need nothing else: decompression
(`Arena\Services\Compress`), RC4 (`Arena\Services\Crypt`), string decryption (`Gw2\Engine\Text\TextDecode.cpp`), the content name hash and the
`token64` codec, and the game values the client shares with the exe code (professions, weapons, game modes).
Folders mirror the exe's `\Code` tree, as in `Gw2.Exe.Objects`. The project references no other project, so the dat
readers and the client can call it. See `gw2-research/docs/archive/compression.md` and
`gw2-research/docs/archive/encryption.md`.
