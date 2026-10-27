# Facility area calculation

The Facility Sizer converts processor counts from a facility solver and queue
metrics from its Queue Results (QR) into floor area. It does not run or resize
the facility solver.

## Inputs

**Facility Area Standards** (`A-STD`) supplies the area assumptions, authored
in the Grasshopper definition:

| Input | Meaning |
|---|---|
| Facility Keys / Area per Unit | A unit key and its installed footprint, m². Keys match solver pool names case-insensitively; tagged identities such as `Counter|DomesticDeparture|Economy` match the `Counter` key. |
| Queue Space per Pax | Queue space for one waiting passenger at that unit, m². Blank uses the default. |
| Default Queue Space per Pax | Used when a unit does not specify queue space. |
| Support % | Operational support area as a percentage of installed-unit area. |
| Circulation %, MEP %, Structure % | Gross-up allowances, applied successively. |
| Fixed Area Names / Fixed Areas | Lump-sum rooms such as a reconciliation room, isolation suite or store. |

**Facility Sizer** (`SIZE`) takes Queue Results, the solver's **Physical per
Pool** counts, optional **Active per Pool** counts, a metric basis, A-STD, and
an optional expansion factor. Counts are expected in the same pool order as
the solver's Queue Results. Pool names come from QR; there are no manual count,
pool-name, key-mapping, or no-physical-area inputs.

The metric basis selects the design hour, worst step, or whole run for queue
peaks. The design step is inferred from QR; there is no separate design-hour
index input. `Total Area` is gross area. Detailed outputs include `Net Area`,
`Support Area`, `MEP Area`, `Structure Area`, `Pool Areas`, `Area Breakdown`,
and `Warnings`.

## Calculation

For each solver pool, its name is matched automatically to an A-STD key:

```
equipment area = solver's installed units x matching area per unit
queue area     = QR peak queue x matching queue space per passenger
support area   = equipment area x support %
net area       = equipment + queue + support + fixed areas
```

An optional expansion factor uplifts net area. Circulation is then calculated
on net area, MEP on net plus circulation, and structure on the area including
MEP. The allowances compound in that order. Installed counts determine floor
area; staffed counts are reported for operations.

A shared pool uses the facility queue peak. A dedicated pool uses its own
queue peak where QR provides it, otherwise its channel peak. If a pool has no
matching A-STD key, the sizer reports a warning and assigns no equipment area
to that pool; it does not silently substitute another standard. Queue area can
still use the default queue-space standard. When A-STD contains exactly one
unit key, that unambiguous standard also covers a shared or otherwise unnamed
single pool.

This is the same area-per-unit method for all facility types. Facility-specific
area formulas (for example, boarding hold-room layouts or security booth
ratios) are not applied by Facility Sizer; processor counts come from the
corresponding facility solver.

## Aircraft stands and gate lounges

**A stand is apron and a lounge is terminal.** The two are counted separately,
because adding a stand to a floor area would be meaningless.

Three components:

- **Gate Inputs** (`G-IN`) - one row per aircraft type: code, domestic and
  international peak-hour movements, stand occupancy, seats, and how the
  planeload divides between sitting, standing and waiting to board.
- **Gate Standards** (`G-STD`) - the policy and the space standards: stand
  buffer, spares, swing stands per code, contact share per group, remote
  stands per bus gate, the max-to-active ratio, and the space each passenger
  kind needs.
- **Gate & Stand Sizer** (`G-SIZE`) - the calculation.

The stands follow the ICAO peak-hour method: a stand is held while an aircraft
is on it, so the requirement is movements multiplied by how long each holds
one. The sector is rounded once over the sum of its codes, so fractions of
different codes can share a stand; the per-code split is rounded on its own,
because a stand serves one code. The difference between the two is reported
rather than hidden.

The lounges are sized on the planeload the aircraft brings: the hold room
(seated and standing, with the next-seat allowance and the open-lounge
reduction), the boarding podiums, and the boarding queue. One lounge per
contact gate, sized by its own code, and one per bus gate, sized as the bus
lounge code.

Outputs: active stands, contact stands, bus gates, remote stands, max stands,
net remote stands, lounge count, hold area, boarding queue area, podium area,
and the net and gross lounge area. The gross uses the same build-up as every
other facility, so the allowances can come from the same `A-STD` set.

This is the peak-hour method. A schedule-derived aircraft-on-ground count is a
different method: it would replace the movements, not the method.

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

Processor counts come from the solver outputs, queue lengths from QR, and area
assumptions from A-STD. The plugin applies the area build-up described above.

The analytic sizing in the TypeScript tool derives its own facility counts and
queue lengths, so its figures will not match these ones even with the same
standards. What matches is the area method and the allowances in the same
order.
