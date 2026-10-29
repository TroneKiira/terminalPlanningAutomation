# Model diagnostics

`Model Diagnostics` audits a finished model without re-running a solver. It
reconciles four things that fail in different ways:

1. **Demand** — what the transfer profile asked for, what the schedule allowed,
   and what the generator actually tagged.
2. **Service eligibility** — who the model rules expect each facility to serve.
3. **Facility output** — who each facility says it served.
4. **Passenger history** — which visits are actually recorded on the passenger.

The result is a text report, per-passenger issue lists, and a long-format CSV.
The component never writes to the model.

## Grasshopper wiring

### Minimum wiring

- `Passengers` — the generated population, or the output of the last facility.
- `Service Results` — one item per facility: `CheckInResults`,
  `SecurityResults`, `EmigrationResults`, `ImmigrationResults`,
  `TerminalEntryResults`, `PassengerVerificationResults`, or raw `QueueResults`.

Every input is optional. With only `Passengers`, the component audits the
transfer rules and the facility visits recorded in passenger history. With only
service results, it audits counts and numeric outputs.

**Bag Drop is a channel inside Check-In**, not a separate component. Do not wire a
`BagDropResults` item. The Bag Drop row is added automatically and its processed
records are read from the `BagDrop` history written by Check-In's BagDrop
channel. That history is collected from the wired population and from every
wired stage output, because Check-In writes it on its processed clones.

`Dwell` has no `QueueResults` output either, so it is added automatically from
the `DepartureLounge` history record when any wired output carries one.

### Recommended wiring

| Input | What it adds |
| --- | --- |
| `Schedule` | Parse errors, missing times/sectors/IDs, passenger-to-leg references |
| `Generation` | Demand versus generated counts, missing Passenger Groups, late check-in |
| `Transfer Demand` | Requested / feasible / effective / tagged reconciliation and peak windows |
| `Service Results` | Expected versus processed passengers for each stage |
| `Facility Sizing` | Peak demand, processor and area checks |
| `Gate Results` | Stand, contact, bus, remote, lounge and area reconciliation |
| `Service Labels` | Human labels in the same order as `Service Results` |

Label a service when the result does not carry passenger history (for example an
empty queue), or when two stages of the same type are wired. Otherwise the type
is inferred from the history keys on the processed passengers, then from the
label.

## Outputs

| Output | Contents |
| --- | --- |
| `Report` | The complete `DiagnosticsReport` object |
| `Status` | `PASS`, `WARN`, `FAIL` or `NOT WIRED` |
| `Summary` | Headline counts, services at a glance, coverage and first steps |
| `Issues` | One block per finding with code, expected/actual and passenger IDs |
| `Service Table` | Incoming, applicable, expected, processed, warm-up, bypassed, missing, extra, missing history, pools, Wait95 |
| `Transfer Table` | Requested, feasible, effective, tagged, population-tagged, outbound/inbound records |
| `Transfer Service Table` | Required, bypassed, no-rules, history-backed processed, missing, served-anyway |
| `Peak Table` | Transfer services at the busiest rolling 60-minute window |
| `Passenger Issues` | One line per record to investigate, grouped by check code |
| `Definitions` | What every count means |
| `Gate Checks` | Stand and lounge reconciliation |
| `Sizing Checks` | Demand, processor and area checks |
| `CSV` | Long-format export, one row per item |

## Count definitions

- **Population** — passenger *records*, not journeys. A schedule-driven transfer
  journey produces a separate arrival record and departure record.
- **Expected** — recomputed from `DHPType`, `Journey`, passenger flags and
  transfer scenario rules. Solvers are not re-run.
- **Processed** — `QueueResults.ProcessedPassengers`. For a history-only stage
  (Bag Drop, Dwell) the list is rebuilt from facility history found on the wired
  population and on every wired stage output, because the visit may be written by
  the stage that owns the channel. Warm-up passengers are included.
- **Warm-up** — simulated, but excluded from `QueuePoolResult.PassengerCount`
  and from queue metrics. This is the usual reason "processed" and "pool" totals
  differ.
- **Missing** — expected but not processed.
- **Extra** — processed but not expected by the independent rule.
- **Missing history** — processed without that facility's `History` entry. The
  visit cannot be audited downstream.
- **Tagged** — outbound departure records tagged by the transfer generator. It
  never counts arrival records, which are separate passengers.
- **Transfer service processed** — a transfer record counts as served when it
  carries the corresponding facility history. Bag Drop served through the
  Check-In `BagDrop` channel is included.
- **Served elsewhere** — expected by the rule, but the incoming population
  already carries that facility's history, so the visit was recorded by another
  stage (for example Bag Drop through Check-In's `BagDrop` channel) or an earlier
  run. Those records are not counted as expected for this result; the service row
  note states how many.

## Reading a service mismatch

- Check the `Channel` on the `Passenger Issues` output/CSV. It is the fastest way
  to see why a record did or did not reach a stage.
- `SVC-011` — expected records are on a channel that no pool in the result
  covers. Add the channel to the facility's `Channels`, or add a pool plan for
  it. The finding details list the count per channel.
- `SVC-012` — online-check-in records on the virtual `Web` channel entered a
  physical Check-In pool. Do not wire `Web` to Check-In: online records with a
  bag use `BagDrop`, online records without a bag need no airport visit.
- `SVC-002` extras are records the facility admitted although their flags say
  they do not need it. The issue row shows `online check-in` and `checked bag`
  so the admitting rule is visible.
- If the Bag Drop row still shows missing records, they carry no `BagDrop`
  history anywhere in the wired results, so Check-In's BagDrop channel did not
  serve them. Their `Channel` shows whether they were routed to that channel at
  all; `SVC-011` lists any channel that no result pool covers.

## Status rules

- `FAIL` — a passenger is missing or extra, a required transfer service has no
  history, a bypassed service was served, or a numeric result contradicts itself.
- `WARN` — history is missing on processed records, pool counts disagree, a
  sizing result has warnings, or demand was not fully tagged.
- `INFO` — capped demand, late check-in, missing optional metadata.
- `NOT WIRED` — no result or history exists for that check. It is not a pass.

`INFO` never changes the headline. `FAIL` outranks `WARN`.

## Check codes

### Coverage and population

| Code | Meaning |
| --- | --- |
| `POP-001` | Null passenger records |
| `POP-002` | Results wired but no passenger population |
| `PAX-100` | Duplicate passenger IDs |
| `PAX-101` | `DHPType` sector and `PassengerType` disagree |
| `PAX-102` | Record is both warm-up and design-step |
| `PAX-103` | Late-for-check-in flag on a non-departure |
| `PAX-104` | No service channel |
| `PAX-105` | No profile name |
| `PAX-999` | Passenger issue list truncated |

### Schedule and generation

| Code | Meaning |
| --- | --- |
| `SCH-001` / `SCH-002` | Schedule parse errors / warnings |
| `SCH-003` | Passenger leg without a usable time |
| `SCH-004` | Passenger leg with an unknown sector |
| `SCH-005` | Passenger leg without an ID |
| `SCH-010` | Passenger references a leg that is not in the schedule |
| `SCH-011` | Passenger has no flight-leg reference |
| `GEN-001` | Generation produced no passengers |
| `GEN-002` | Supplied population differs from generation output |
| `GEN-003` | `GeneratedPassengerCount` is stale |
| `GEN-004` | DHPType counts differ from the generation result |
| `GEN-010` | Leg generated fewer passengers than demand |
| `GEN-020` | Generation warnings |
| `GEN-021` | Legs skipped for a missing Passenger Group |
| `GEN-030` | Passengers reach check-in after last admission |
| `GEN-031` | Passengers arrive before check-in opens |

### Services

| Code | Meaning |
| --- | --- |
| `SVC-000` | No service results wired |
| `SVC-001` | Expected record not processed |
| `SVC-002` | Processed record not expected |
| `SVC-003` | Duplicate processed records |
| `SVC-004` | Processed without facility history |
| `SVC-005` | History on an unprocessed record |
| `SVC-006` | Processed with no active processor |
| `SVC-007` | Pool counts differ from non-warm-up processed |
| `SVC-008` | Queue metrics without processed passengers |
| `SVC-009` | Processed record absent from the output population |
| `SVC-010` | Stage population differs from the queue output population |
| `SVC-011` | Expected records are on channels that no result pool covers |
| `SVC-012` | Virtual `Web` channel served at Check-In |
| `SVC-020` | Verification gates have no throughput |
| `SVC-021` | Verification has demand but no gates |
| `SVC-022` | Verification utilization outside 0–100% |
| `DWL-001` | Dwell was not audited (no lounge history or result) |

### Transfers

| Code | Meaning |
| --- | --- |
| `TRF-001` | Transfer records exist but no demand result |
| `TRF-010` | Effective demand not fully tagged |
| `TRF-011` | More tagged than effective demand |
| `TRF-012` | Tagged count differs from the population |
| `TRF-013` | Demand capped by the schedule |
| `TRF-014` | Inbound and outbound transfer record counts differ |
| `TRF-015` | Tagged transfers without explicit processing rules |
| `TRF-016` | Transfer demand warnings |
| `TRF-100` | Transfer record without a scenario |
| `TRF-101` | Scenario set but journey is not Transfer |
| `TRF-102` | Transfer rules on a non-transfer record |
| `TRF-SVC-001` | Required transfer service has no history |
| `TRF-SVC-002` | Bypassed transfer service was served |
| `TRF-SVC-003` | Transfer service has demand but no result |
| `TRF-SVC-010` | Demand-result service usage is stale against the population |
| `TRF-PEAK-001` | Required service short at the peak |
| `TRF-PEAK-002` | Bypassed service served at the peak |

### Gates and sizing

| Code | Meaning |
| --- | --- |
| `GATE-001` | Gate sizing warnings |
| `GATE-002` | More active stands than total stands |
| `GATE-003` | Lounge areas do not sum to net area |
| `GATE-004` | Stand bucket sum differs from the total |
| `GATE-005` | Contact, bus or remote stands do not reconcile |
| `GATE-010` | Gate result is PHP-mode while a schedule is wired |
| `GATE-011` | Stands sized but no lounges |
| `SIZE-001` | Demand but no processors |
| `SIZE-002` | Gross area below net area |
| `SIZE-003` | Sizing warnings |

## Limits

- `Walking` writes no passenger history, so it cannot be audited from the
  population. Wire its queue result if you need its counts.
- `Transfer Desk` and `Baggage Recheck` have no facility solver. The transfer
  service table reports them as `NOT WIRED` and still states the rule demand.
- A transfer arrival record and its departure record have no persistent journey
  link. Arrival and departure counts are therefore compared as populations, not
  matched one-to-one.
- Diagnostics reads solver outputs. It does not re-run optimization, so changing
  an input without re-running the model will show stale results.

## CSV columns

`Section, Item, Code, Status, Severity, Expected, Actual, Variance, Unit,
PassengerIds, Message`

Sections are `Summary`, `Finding`, `Service`, `Transfer`, `TransferService`,
`Peak`, `PassengerIssue`, `Gate`, `Sizing` and `Definition`. Filter
`Section = PassengerIssue` to get one row per record to investigate. Each
passenger row appends the flight leg, `DHPType`, journey, scenario, channel and
the online-check-in / checked-bag flags to the message.
