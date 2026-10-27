# Facility area calculation

How a facility's floor area is built from the queue simulation's results, and
how to wire it in a Grasshopper definition.

## What is not touched

The queue simulation is unchanged. `QueueSimulator`, the capacity optimizer and
the queue metrics are exactly as they were, and the queue results a facility
produces are used as they are. The sizing components read them; they do not
re-run anything.

## The two pieces

**Facility Area Standards** (`A-STD`) — the numbers, authored in the
definition so they are visible and editable:

| Input | Meaning |
|---|---|
| Facility Keys | One name per kind of installed unit: `Counter`, `Kiosk`, `BagDrop`. Matched against the facility's pool names, case insensitively. |
| Area per Unit | Floor area of one installed unit, m². Excludes general circulation, which is an allowance. |
| Queue Space per Pax | Queue space for one waiting passenger at that unit, m². Blank means the default is used. |
| Default Queue Space per Pax | Used when a unit does not state its own, m². |
| Circulation % | Of the net area. |
| MEP % | Of the area including circulation: plant rooms, risers and services. |
| Structure % | Of the area including MEP. |
| Fixed Area Names / Fixed Areas | Rooms added as a lump rather than sized from a count: a reconciliation room, an isolation suite, a store. |

**Facility Sizer** (`SIZE`) — the calculation. New inputs:

| Input | Meaning |
|---|---|
| Area Standards | The `A-STD` set above. Optional. |
| Pool Names | The facility's routed pool names, in output order. Defaults to the pool names in the Queue Results. |
| Physical per Pool | Installed units per pool, in the same order. Defaults to the Queue Results, pool by pool. |
| Active per Pool | Staffed units per pool, in the same order. Defaults to the Queue Results. |
| Facility Key per Pool | The unit standard each pool is sized with, in the same order. Empty means the pool is matched by its own name. |
| No Physical Area | Marks a pool as a route with no physical counterpart, in the same order. |
| Use Routed Pool Areas | On: each pool is sized from its own unit standard. Off: the facility's own sizing rule is used. |
| Expansion Factor | Uplift on the net area before the allowances, as a fraction (0.10 = 10%). |

New outputs: `Net Area`, `MEP Area`, `Structure Area`, `Gross Area`,
`Support Area`, `Pool Areas`, `Area Breakdown`, `Warnings`.

`Total Area` is now the gross area, and the breakdown reconciles to it.

## The area build-up

```
net         = queue + processing + support + fixed areas
              (net is multiplied by (1 + expansion factor) when one is given)
circulation = net x circulation %
mechanical  = (net + circulation) x MEP %
structure   = (net + circulation + mechanical) x structure %
gross       = net + circulation + mechanical + structure
```

The three allowances are applied in that order, and each is a share of the area
that already includes the ones before it, so they compound. The `Area
Breakdown` output states each step.

Operational support — staff space, back of house, the check-in rear working
area — is **not** the same thing as MEP. Support is a room you can point at;
MEP is an allowance. They are separate lines and neither stands in for the
other.

## Routed pool areas

With `Use Routed Pool Areas` on, each routed pool is sized on its own:

```
equipment area = installed units in the pool x m2 per unit for that key
queue area     = passengers waiting in the pool x m2 per passenger for that key
```

and the facility's areas are the sum over the pools. This is what makes
channel routing reach the building: counters, kiosks and bag-drop units are
built from the units each pool actually has, rather than from one generic
footprint multiplied by a total processor count.

Rules the calculation follows:

- **Installed units, not staffed units.** The floor area is built from the
  physical units. The staffed count is reported alongside, because it is the
  operational number.
- **A shared pool is sized on the facility's peak queue**, because everybody
  waits in the same queue. **A dedicated pool is sized on its own channel's
  peak**, so separate channels are not sized on each other's queues. The
  channel peaks may fall at different times, so the sum is a conservative
  figure.
- **A pool with no matching standard** is sized with the facility's own unit
  size and reported as a warning, rather than dropped.
- **A channel with no queue metric** falls back to the facility peak and is
  reported as a warning.
- **Counts the definition leaves out are taken from the Queue Results**, pool
  by pool and matched by name, so naming the pools without naming their
  counts still works. An explicitly wired count always wins.
- **A pool can be mapped to a different standard** with *Facility Key per
  Pool*, so a channel called "Kiosk East" is sized with the "Kiosk" standard.
- **A route with no physical counterpart** - an online check-in, say - is
  marked with *No Physical Area*. It contributes no equipment area, and the
  result says so.
- In routed mode the facility's own equipment rules are not run, so the
  equipment counts are stated per pool instead. The per-pool lines are in the
  `Pool Areas` output.

### Which facilities can use routed areas

Routed sizing is refused by a facility whose area is not simply its units
plus the queue in front of them. **Check-In and Bag Drop** support it. The
others - Security, Immigration, Boarding, Retail, Passenger Verification -
keep their own rules until each has a routed design of its own, and the
result says that routed areas were not used.

Which run of the simulation is read follows the `Metric Basis` input: the
design hour, the worst step, or the whole run. The queue areas follow the
same basis as the wait figures.

## Aircraft stands and gate lounges

**A stand is apron and a lounge is terminal.** The two are counted
separately, because adding a stand to a floor area would be meaningless.

Three components:

- **Gate Inputs** (`G-IN`) - one row per aircraft type: code, domestic and
  international peak-hour movements, stand occupancy, seats, and how the
  planeload divides between sitting, standing and waiting to board.
- **Gate Standards** (`G-STD`) - the policy and the space standards: stand
  buffer, spares, swing stands per code, contact share per group, remote
  stands per bus gate, the max-to-active ratio, and the space each passenger
  kind needs.
- **Gate & Stand Sizer** (`G-SIZE`) - the calculation.

The stands follow the ICAO peak-hour method: a stand is held while an
aircraft is on it, so the requirement is movements multiplied by how long each
holds one. The sector is rounded once over the sum of its codes, so
fractions of different codes can share a stand; the per-code split is
rounded on its own, because a stand serves one code. The difference between
the two is reported rather than hidden.

The lounges are sized on the planeload the aircraft brings: the hold room
(seated and standing, with the next-seat allowance and the open-lounge
reduction), the boarding podiums, and the boarding queue. One lounge per
contact gate, sized by its own code, and one per bus gate, sized as the bus
lounge code.

Outputs: active stands, contact stands, bus gates, remote stands, max
stands, net remote stands, lounge count, hold area, boarding queue area,
podium area, and the net and gross lounge area. The gross uses the same
build-up as every other facility, so the allowances can come from the same
`A-STD` set.

This is the peak-hour method. A schedule-derived aircraft-on-ground count is
a different method: it would replace the movements, not the method.


## Values that need a decision

- **Whether a unit's m² includes its own working envelope.** If it does, and
  the circulation allowance is applied on top, circulation is counted twice.
  The convention used here is that a unit's area is the unit and its own
  working space, and general circulation is the allowance.
- **Which values are adopted from IATA or another reference.** Any figure taken
  from a published standard should be recorded with its source and page, and
  confirmed for this project rather than assumed. Nothing in the plugin
  hard-codes a reference value: every figure is an input.
- **Whether fixed rooms are needed** for a facility, and how big they are. They
  are entered as named areas, separately from the MEP allowance.

## Where the numbers come from

The sizing consumes the queue results as they are. The counts and the queue
lengths are the simulation's; the standards and the allowances are the
definition's; the area build-up is the plugin's.

Note that the analytic sizing in the TypeScript tool derives its own facility
counts and queue lengths, so its figures will not match these ones even with
the same standards. What matches is the method: the same area build-up, the
same routed pools, the same allowances in the same order.
