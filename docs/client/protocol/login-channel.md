# Login channel: new client

How the new client implements [Login channel](../../../gw2-research/docs/protocol/login-channel.md). The retail behaviour and its evidence are in that note.

## Send timing up to character select (recorded 2026-11-01)

Replayed by `Gw2.Client.Net.Login.LoginSession` (`LoginSessionTests`): every flush matches the recorded bytes and tick
on both 00:49 sessions.

## Install id and system record

`SystemRecord.FromThisMachine` builds the same record; on the recording machine every field equals the recorded one
except the measured MHz (4680 against 4691 and 4693 in two recorded sessions).
