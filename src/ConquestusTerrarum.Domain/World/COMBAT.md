# Domain combat MVP

Simulation is lazy: WorldSimulator.AdvanceTo jumps to the earliest arrival, slot
death, or requested target. There are no ticks or background services. All active
battles share the same timeline.

At each boundary T the simulator performs these phases:

1. A: advance every occupied slot's continuous damage to T.
2. B: resolve simultaneous deaths and release slot survivors together.
3. C: remove destroyed armies from locations and movement.
4. D: apply all arrivals at T as one batch.
5. E: reconcile battle existence and each army's pairwise hostile participation.
6. F: refill vacant slots from idle living units using deterministic randomness.
7. G: resolve ownership for ended battles using surviving entry timestamps.
8. H: commit the world checkpoint.

Arrivals at a death boundary are included before battle lifetime and ownership are
resolved. If hostile arrivals replace the casualties, the existing battle continues.
An occupied valid slot continues until a death or withdrawal; arrivals fill only
vacant slots. After a death, its survivor returns to the random candidate pool.

Units require explicit IDs, HP and DPS; zero DPS is permitted. Empty or entirely
dead armies are destroyed. HP never regenerates. Retreat commands first advance
to their timestamp, then kill all wounded living units. If any healthy units remain,
normal retreat travel starts. Otherwise the army is destroyed without movement.
Existing retreat destination and lock rules remain in force.

Location.CombatSlotCount defaults to one and must be positive. Pair eligibility
comes from WorldState.AreHostile, supporting alliances and free-for-all battles.
Candidate pairs are enumerated in explicit army/unit ID order, then chosen with
SplitMix64 and unbiased rejection sampling. IDs establish a stable candidate order;
they do not determine ownership ties. WorldState.RandomSeed and Battle.RandomState
are explicit domain state. Battle seeds use the world seed, location bytes in an
explicit byte order, and UTC battle-start ticks.

CombatSlot stores army/unit identities, pairing timestamp and starting HP. Damage
is recomputed from these anchors, so checkpoint frequency cannot accumulate rounding
error. Exact integer arithmetic on decimal coefficients determines time-to-death;
a fractional tick rounds UP to the first representable DateTimeOffset tick. Deaths
with equal representable timestamps resolve together. Positive HP rounds upward at
decimal precision to avoid premature deaths; death timestamps remain authoritative.
Zero DPS and deaths beyond the DateTimeOffset range have no scheduled boundary.

The world checkpoint, units/HP, army movement/entry/retreat-lock state, active battle
start/random state and slot anchors together describe what a future persistence
adapter must restore. No persistence adapter or serialization framework is added.

After battle, the earliest surviving entry determines ownership. Equal earliest
entries from different players leave OwnerId null. If there are no survivors, the
existing location owner remains unchanged, preserving the previous ownership rule.
