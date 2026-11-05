# Security: E-Gate → ATRS in series

## Two simulation modes

The Security component simulates the checkpoint in one of two ways, selected by
the `Series E-Gate + ATRS` (SER) input:

**Single stage (default).** Every screened passenger joins one shared lane pool
(or the channel pools wired through `Channels` / `Pool Plans`). This is the
legacy behaviour and is unchanged.

**Series.** The checkpoint is two real queues in series:

```
arrivals ──► [ EGate ]  ── walk ──►  [ ATRS ]  ──► departures
             stage 1                 stage 2
```

- **Stage 1 — entry e-gates**, pool identity `EGate`
- **Stage 2 — ATRS screening lanes**, pool identity `ATRS`

Every stage-1 departure becomes a stage-2 arrival, so each passenger's
checkpoint time is:

```
gate queue + gate service + walk + lane queue + lane service
```

A slow screening stage shows up as a queue *at the ATRS stage*, and the
checkpoint's effective throughput is the slower stage — not a merged rate.

## Inputs

| Input | Meaning |
|---|---|
| `Passengers` (PAX) | population entering security |
| `Service Profile` (SP) | the one security process time. Series mode: this is the ATRS stage time; the e-gate stage uses `E-Gate Service Time` or an `EGate` key on `Service Profiles` |
| `Security Lanes` (L) | ATRS lane PNR, used when Optimize is off and no `ATRS` plan is wired |
| `Optimize Lanes` (O) | size each stage to its target wait |
| `Target Wait` (TW) | MQT in minutes; applied to the lanes and, in series mode, to the ATRS stage |
| `Staffing %` / `Seed` | as usual |
| `Service Profiles` (SP-MAP) | optional per-passenger times; in series mode the keys `EGate` and `ATRS` give each stage its own |
| `Pool Plans` (PP) | optional pool standards; in series mode use the identities `EGate` and `ATRS` |
| `Series E-Gate + ATRS` (SER) | turns the two-stage model on |
| `E-Gate Gates` (EG) | physical entry e-gates (stage 1 PNR) when no `EGate` plan is wired |
| `E-Gate Target Wait` (ETW) | stage 1 MQT in minutes; 0 means no target |
| `E-Gate Service Time` (ES) | stage 1 mean service time in seconds, used when the map has no `EGate` key |
| `E-Gate to ATRS Walk` (EW) | walk from the gate to the screening lane, minutes; delays stage-2 arrivals only |

A wired `EGate` Pool Plan still overrides `EGate Gates` / `E-Gate Target Wait`,
and an `EGate` key on `Service Profiles` still overrides `E-Gate Service Time`.

Removed from earlier versions as redundant: `Channels` (Pool Plans or one
shared pool covers it) and `Resource Domain` (Pool Plans carry PDOMAIN; the
default search is 1–1200).

## Built-in series defaults (no pool plan wired)

| Stage | PNR | MQT | Service time |
|---|---|---|---|
| `EGate` | `E-Gate Gates` (1) | `E-Gate Target Wait` (2 min) | `E-Gate Service Time` (7 s) |
| `ATRS` | first `Security Lanes` value | first `Target Wait` value | the wired Service Profile |

## Pool identities

| Stage | Pool Plan identity | Fallback when no plan is wired |
|---|---|---|
| Entry e-gates | `EGate` | built-in defaults above |
| ATRS screening | `ATRS` | `Security Lanes` (PNR), `Target Wait` (MQT), Service Profile (time) |

Wire `Pool Plans` rows keyed `EGate` and `ATRS` to control PNR, MQT and the
resource domain per stage. A shared plan is ignored in series mode with a
warning: two queues in series need two explicit rows.

A plan with **MQT = 0** means "no target": optimization keeps that plan's
nominal resources (clamped into its domain) instead of growing to the domain
maximum.

## Splitting each stage by demand class (four pools)

A stage keeps **every** plan keyed to its channel, so domestic and
international passengers can queue separately at each stage with
`PoolKey` splits, exactly like Check-In:

| Pool identity | Queue |
|---|---|
| `EGate\|DomesticDeparture\|Domestic Economy` | domestic e-gates |
| `EGate\|InternationalDeparture\|International Economy` | international e-gates |
| `ATRS\|DomesticDeparture\|Domestic Economy` | domestic screening |
| `ATRS\|InternationalDeparture\|International Economy` | international screening |

Notes:

- The PoolKey must be `DHPType|ProfileName` (e.g. `DomesticDeparture|Domestic
  Economy`). A single word like `EGate|Domestic` is read as a profile name and
  matches nobody; the solver warns how many passengers matched no pool.
- The Service Time Map takes the same keys, so each sector at each stage can
  carry its own time. Tag the rows `Security` to keep them away from
  Emigration's `EGate`.
- `Pool Names`, `Physical per Pool`, `Active per Pool`, `Wait 95 per Pool`,
  `Nominal per Pool` and `Search Outcomes` all carry one entry per pool, in
  stage order (`EGate|…` rows, then `ATRS|…` rows).
- Each pool meets its own MQT inside its own resource domain; the Facility
  Sizer sizes each pool's units and queue area separately.

## Facility-scoped values

The same identity can carry **different values at different facilities**. Tag a
`Pool Plans` row (or a Service Time Map row) with a facility name — `Security`,
`Emigration`, … — and only that facility uses it:

| Facility | Identity | PNR | MQT |
|---|---|---|---|
| Emigration | `EGate` | 4 | 5 min |
| Security | `EGate` | 2 | 2 min |

Rules:

- A row tagged for a different facility is ignored by this facility.
- An untagged row applies to every facility.
- A facility-tagged row wins over an untagged row with the same identity.
- Service times work the same way: map key `EGate` tagged `Emigration` (25 s)
  and key `EGate` tagged `Security` (7 s) resolve independently per stage.

Without a Service Time Map, series mode falls back to the input's `E-Gate
Service Time` for stage 1 and the legacy `Service Profile` for stage 2; a
warning names the assumption.

## Transfers

Passengers whose scenario rules request a separate transfer-security pool keep
that pool identity (default `Transfer Security`) at the ATRS stage — they still
pass through the e-gate stage first. Wire a Pool Plan for the transfer channel
to size that pool independently; otherwise they join ATRS with a warning.

## Outputs

Series mode reports both stages through the standard pool outputs — no extra
pins: `Pool Names` lists `EGate` then `ATRS`, and `Physical per Pool`,
`Active per Pool`, `Wait 95 per Pool`, `Nominal per Pool` and `Search Outcomes`
all follow that order.

The aggregate outputs keep their meaning: `Physical Lanes` is the sum of both
stages, and `Physical per Pool` / `Pool Names` list `EGate` then `ATRS`, so the
Facility Sizer sizes each stage from its own area standard (e.g. `EGate` =
2 m²/unit, `ATRS` = 220 m²/unit) with each stage's own queue area.

Per passenger, the output population carries:

- `SecurityEntryRoute` / `SecurityScreeningRoute` — the stage each passenger used
- `SecurityEntryWait` / `SecurityScreeningWait` — the wait at each stage
- `WaitTime` — the screening-stage wait (the stage recorded in `History`)

When either stage's Wait95 is more than twice the other's, the solver names the
slower stage as the checkpoint bottleneck. The e-gate case also notes that ATRS
is sized on the arrivals the gates release, so a throttled gate spreads demand
and understates the screening lanes the raw demand needs.

## Facility tags (share one list safely)

`Pool Plans` and the `Service Time Map` both accept a `Facility` tag per row, so
one shared list can carry `EGate` at Security (7 s) and `EGate` at Emigration
(25 s). Tags are matched **leniently** — case, spacing, hyphens and the
planning-table spellings all work:

| Tag spelling | Resolves to |
|---|---|
| `Security`, `Security Check`, `Security Screening`, `Security Checkpoint` | Security |
| `Emigration`, `Departure Emigration`, `Outbound Passport Control` | Emigration |
| `Immigration`, `Arrival Immigration`, `Inbound Passport Control` | Immigration |
| `Check-In`, `Check-in Area`, `Check In` | Check-In |
| `Terminal Entry`, `Terminal Entry Gates` | Terminal Entry |
| `Bag Drop`, `Baggage Drop` | Bag Drop |
| `Transfer Desk`, `Transfers Desk` | Transfer Desk |
| `Baggage Recheck`, `Bag Recheck` | Baggage Recheck |

Failures are loud, never silent:

- A tag that matches **no** facility is reported ("matches no facility … valid
  names: …") and the row is ignored.
- A facility whose scope ends up **empty** while rows exist elsewhere is
  reported ("No pool plan / Service Time Map row belongs to …"), naming the
  tags that do exist.
- An **untagged** row that looks like another facility's stage key (for example
  `ATRS|…` wired to Emigration) is flagged before it can retime that facility's
  passengers. `EGate` is shared between Emigration and Security by design, so
  tag those rows when you use sector splits.

## Sharing the Service Time Map safely

Route identities are shared across facilities (`EGate` exists at Emigration and
at security entry), and the key lookup is channel-first, so a sector key
`EGate|InternationalDeparture` shadows a plain `EGate` key at **every** facility
that sees channel `EGate`. Rule of thumb: **tag every stage-key row with its
facility**; leave untagged only genuinely global rows (a bare profile name, for
instance).

## Notes and limits

- Passengers do not abandon queues or re-enter the e-gate stage.
- The walk between stages is a pure delay (`E-Gate to ATRS Walk`, default 0);
  it is not modelled as a capacity.
- With `Series` off, results are identical to the previous release.
- Passenger Details exposes the split: `Security Entry Route`,
  `Security Screening Route`, `Security Entry/Screening Wait`, and the e-gate
  start/end times. The Security history record carries the e-gate visit in its
  entry fields and the screening visit in its main fields, each with its own
  pool name.

## Timing contract: no hidden delays

- **Terminal entry → Check-In is strictly sequential.** The historical
  `TerminalEntryWalkMinutes` is no longer folded into the next stage's
  arrival time; the walk stays on the passenger as data. Passenger Details
  exposes `Walking Minutes`, so a narrative can show it explicitly
  ("+ 0.9 min walk").
- **Verification** stamps a visit on everyone it receives (it is an
  analytical stage, not a queue) and labels itself "Verification" — it no
  longer reports "(no pool)". It warns when it receives no passengers, which
  usually means the Passengers wire skips this population.
- **Passenger Details "Blank When No History" (BWH):** with a Facility Type
  selected, a passenger without that facility's history outputs NaN instead
  of the stale previous-facility values. Combined with the `Has History`
  output, a journey narrative can skip stages the passenger never visited —
  the skipped stage stops reading as a second, simultaneous visit.

## Sizing vs realized Wait95 (series mode)

With `Optimize Lanes` on, security runs two passes per solve:

- **Sizing (design) pass** — each stage's capacity is searched against the
  **raw security demand** (the arrivals the checkpoint is meant to handle),
  with the wired per-pool MQT as the target. Stage 2's search therefore does
  not see the e-gates' throttled releases, so a tight e-gate stage can never
  shrink the screening requirement.
- **Realized pass** — the final simulation runs on the true staged arrivals
  (e-gate releases → walk → screening), at the capacities the sizing pass
  chose, so per-passenger waits reflect the actual checkpoint behaviour.

The optimizer `Search Outcomes` output carries the design-pass trace; the
facility `Wait 95` values are the realized ones. `Nominal per Pool` reports
the wired PNR, not the optimized count.
