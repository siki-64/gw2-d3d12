# Split archive store: new client

How the new client implements [Split archive store](../../../gw2-research/docs/archive/split-archive.md). The retail behaviour and its evidence are in that note.

## Remake reader

`FileArchive` reads from any readable, seekable `Stream` (`FileArchive.Open(Stream)`). `Open(path)` picks
`SplitStore` when the path is a store root (a directory holding `0-1-100`), and a `FileStream` otherwise.
The split stream maps logical offsets to block files with the shipped `1 MiB` / `100` values, reads short or missing
blocks as zeros, measures the logical size as in [Logical size](../../../gw2-research/docs/archive/split-archive.md#logical-size), and does not take `Lock.dat`.
