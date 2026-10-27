# Terminal Entry Queue

The **Terminal Entry** stage simulates the admission checkpoint between the
terminal door and check-in. It is a queue, not a throughput calculator: passengers
wait for a manual-verification position or a DigiYatra/e-gate, and the stage
reports the resulting peak queue, wait distribution and queue history.

This is separate from the existing **Passenger Verification** component, which
remains an analytical throughput check and does not simulate a queue.

## Wiring

```text
Schedule Passenger Generator (PAX)
  -> Terminal Entry (PAX)
  -> optional Walking (entry to check-in)
  -> Check-In
```

Use **Arrival Basis = Terminal Entry** on the schedule generator. The generator
records the original terminal-entry time on the passenger, so the entry stage
queues at the checkpoint even when the generator already added an
entry-to-check-in walk.

If `Apply Entry Walk` is on, the entry stage:

1. puts the passenger back at the recorded terminal-entry time,
2. simulates the entry queue,
3. re-applies the recorded walk after service.

For a more visible model, leave `Apply Entry Walk` off and use a separate
**Walking** component between Terminal Entry and Check-In instead.

Any departing passenger carrying a valid `TerminalEntryArrivalTime` is queued
at that time, including passengers that are not linked to a schedule leg. This
keeps the checkpoint ahead of any clock advance already applied upstream.

Schedule passengers generated with **Arrival Basis = Check-In Queue** have no
terminal-entry timestamp. The stage passes them through and warns, rather than
silently inventing an entry time.

## Routing

Departing passengers are split between two pools:

| Pool | Default service time |
|---|---:|
| Manual Verification | 15 s/passenger |
| DigiYatra | 7 s/passenger |

`DigiYatra Share` is the percentage assigned to the e-gate pool; the remainder
is manual. The default is 37.5%, matching the 375/625 split in the current
terminal-entry table. The split is made on the realized departing population
with the solver seed, so a fixed seed reproduces the same allocation.

The assigned route is stored on the passenger as `TerminalEntryRoute`. The
passenger's `Channel` is preserved for check-in routing; the entry stage uses
that field temporarily only on its private working copies.

## Service times

Service times are supplied the same way as Check-In:

- **Service Time Map** (preferred) resolves a profile per passenger. Use
  `Manual Verification` and `DigiYatra` as keys to give each route its own
  profile.
- **Service Profile** is a single fallback used only when no map is wired.
- With neither wired, the stage warns and uses the built-in route defaults:
  Manual Verification 15 s/passenger, DigiYatra 7 s/passenger.

## MQT

Each pool's MQT is supplied through its **Pool Plan**, interpreted as the
**target whole-run 95th percentile wait in minutes**. With **Optimize Positions**
off, the nominal position counts are simulated as entered. With it on, each
pool is searched independently until its MQT is met inside the Resource
Domain, and the result warns if the target is unreachable.

The values below are also the fallback when no Pool Plan is wired:

| Pool | Nominal positions | MQT |
|---|---:|---:|
| Manual Verification | 3 | 3 min |
| DigiYatra | 1 | 2 min |

## Outputs

The component returns:

- the passenger population advanced to check-in,
- the passengers served by the entry pools,
- the complete `QueueResults` (including queue timeline and per-pool history),
- physical and active positions,
- facility-wide peak queue, average/max/P95 wait and utilization (pooled across both entry routes),
- per-pool names, positions, average/max/P95 wait, peak queue and optimizer search outcomes.

The facility sizer accepts `Facility Type = 7 (Terminal Entry)` and can size the
queue area from the simulated peak queue and the processing area from the
installed positions. With `Use Routed Pool Areas` on, `Manual Verification` and
`DigiYatra` can each use their own unit and queue-space standards.

## Assumptions and limits

- Only departing passengers use terminal entry; arriving passengers pass through.
- The DigiYatra share is a planning input, not an eligibility model. Age,
  nationality and enrolment rules are not simulated.
- Service times are lognormal. The mean and standard deviation come from the
  resolved Service Profile; the built-in route defaults use zero dispersion.
- Passengers do not abandon the queue or switch between manual and e-gate pools.
- The entry stage does not model kerbside drop-off or the walk from the curb to
  the door. Add a Walking segment before Terminal Entry if that time is needed.
