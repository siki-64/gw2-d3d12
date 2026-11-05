# Own character state (`0x12`): new client

How the new client implements [Own character state (`0x12`)](../../../gw2-research/docs/protocol/own-character-state.md). The retail behaviour and its evidence are in that note.

## Jumps

`Gw2Client` jumps on Space with that arc and lands on the collision ground; `OwnCharacterSteps` reports the states
(test against capture 000717).

## Dodges

`OwnCharacterMovement.CommandsToSend` builds both (test against capture 000717). `AgentEndurance` keeps `0xd9`/`0xdb`
per agent and `Gw2Client` dodges only while the points reach the cost.

## When it is sent

`OwnCharacterMovement` builds every recorded move whose motion counter is 0 (over 80% of them) field for field.
`OwnCharacterMovement` builds every recorded `0x04` whose motion counter is 0 field for field.
