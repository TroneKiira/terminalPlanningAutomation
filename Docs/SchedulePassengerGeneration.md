# Schedule Passenger Generation

This workflow generates passengers from a design-day flight schedule while keeping the existing queue, facility, and sizing components unchanged.

## Canvas wiring

```text
Excel Path panel
  -> Flight Schedule Import (FS)
  -> Schedule Passenger Generator (FS)

Passenger Profile(s)
  -> Create Schedule Passenger Group (CSPG; profiles + Type 0-3, no DHP)
  -> Schedule Passenger Generator (DD / DA / ID / IA)

Check-In Window Rules
  -> Schedule Passenger Generator (CIW)

Walking Profile (optional; defaults to 1.35 m/s)
  -> Schedule Passenger Generator (WP)

Schedule Passenger Generator (PAX)
  -> Terminal Entry (optional but recommended for departures)
  -> optional walking segment(s)
  -> existing Check-In / Bag Drop / Security / Emigration components

Schedule Passenger Generator (PGR)
  -> Schedule Peak Profile (chart)
```

`Flight Schedule Deconstruct`, `Arrival Legs`, and `Departure Legs` are inspection outputs. They are not required for generation.

Tagged transfer passengers bypass the Terminal Entry stage: only originating
departures are queued at the entry gates. Transfers continue on the Terminal
Entry `Passengers` output to downstream facilities, and the component reports
them on `Transfers Bypassing Entry`.

## Groups without a manual DHP

Schedule mode does not require `Create Passenger Demand`. Use
**Create Schedule Passenger Group (CSPG)** instead:

- Wire the behaviour profiles to `Profiles`.
- Set `Type`: `0` Domestic Departure, `1` Domestic Arrival,
  `2` International Departure, `3` International Arrival.
- Wire the group to the matching generator port.

CSPG has no DHP input, so it cannot fail on a missing D. The existing
DHP-based Windowed Passenger Generator still uses `Create Passenger Demand` and
`Create Passenger Group`.

## Windowed demand basis

`Create Passenger Demand` defaults to `Demand Basis = 1` (**Flight Hour**): each
hourly factor describes passengers on flights whose STD (departures) or STA
(arrivals) falls in that hour. Each passenger samples an STD/STA inside the
hour; departure passengers are placed backwards using their profile's advance
interval and show-up curve, while arrival passengers are placed at
`STA + Arrival Delay`. This is a passenger-level approximation, not a
synthesized flight schedule. Check-in windows use sector defaults because no
airline or flight identity is available.

Set `Demand Basis = 0` (**Terminal Arrival**) to retain the legacy behavior,
where each factor describes passengers arriving at the terminal in that same
hour. The warm-up inputs apply only to this legacy basis; Flight Hour demand
does not add synthetic warm-up passengers because departures may arrive before
the analysis window naturally. The design-hour tag remains attached to the
selected flight-demand step, and facility Wait95 targets remain whole-run.

## Schedule demand outputs

The Schedule Passenger Generator exposes the schedule-derived peak values:

- `DD Demand`, `DA Demand`, `ID Demand`, `IA Demand`
- `Combined Demand`
- A peak time output for each.

Peaks use rolling 60-minute windows stepped every 5 minutes, matching the
TypeScript peak-profile page. A window is labelled by its start minute. The
combined curve counts all four flows in the same window; it is not the sum of
four separate peaks.

The `Schedule Peak Profile` chart plots the same data:

- View `0`: demand at scheduled flight times (STD/STA).
- View `1`: generated passenger arrivals after show-up, walking and arrival
  timing.
- Series: domestic departures, domestic arrivals, international departures,
  international arrivals and combined.

## Departure timing

For each departure leg:

- PAX comes from the schedule. If PAX is missing, `seats x load factor` is used.
- The flight's PAX is split across the Passenger Profiles by their percentages.
- Each profile has a Min Advance and Max Advance relative to the actual STD.
- Its Show-Up Profile CDF spans:

```text
earliest terminal entry = STD - Max Advance
latest terminal entry   = STD - Min Advance
```

The Arrival Profile workbook curve is the primary source for this shape. Leisure, Business, Mix, and Custom presets are supported. The CDF reaches 100% at the latest entry time, so all schedule PAX are assumed to arrive.

`Arrival Basis` controls the meaning of the generated time:

- `0` Terminal Entry
- `1` Check-In Queue

When `Apply Entry Walk` is on, the generator adds `distance / walking speed` to terminal-entry passengers. Do not add the same walking segment again downstream.

The generator also records the original checkpoint time on every terminal-entry
passenger. If a **Terminal Entry** stage is used, it queues at that recorded
time and re-applies the walk after entry service. This keeps the entry queue
ahead of the walk even when the generator has already advanced the passenger to
check-in. For a more visible definition, leave `Apply Entry Walk` off and add a
separate Walking component between Terminal Entry and Check-In.

With `Arrival Basis = Check-In Queue`, passengers have no terminal-entry
timestamp; the Terminal Entry stage passes them through with a warning.

## Check-in windows

`Check-In Window Rules` defaults follow `MODELS_REPORT.tex`:

| Sector | Opens | Last admission |
|---|---:|---:|
| Domestic | STD - 120 min | STD - 60 min |
| International | STD - 180 min | STD - 60 min |

Closing means last admission. Passengers admitted by then may finish service.
Passengers projected after last admission are flagged and are not served by
Check-In. Early arrivals are flagged and wait in the hall until check-in opens;
that pre-opening time is not counted as check-in queue wait.

Optional override lines:

```text
Airline,Opens,LastAdmission
Airline,FlightNumber,Opens,LastAdmission
```

Times are minutes before STD.

## Arrival timing

STA is the aircraft's scheduled arrival, not the passenger's arrival at immigration or baggage reclaim. The generator places arrival passengers at:

```text
STA + Arrival Delay
```

`Arrival Delay Min` and `Arrival Delay Max` are a visible, uniform assumption. They are deliberately separate from deplaning W15/W30/W60 values, which cannot uniquely determine minute-level timestamps.

## Deplaning profiles

`Deplaning Profile` can prefill from target annual mppa:

| Target annual mppa | Default | W15 / W30 / W60 |
|---|---|---|
| <= 10 | Small | 45 / 70 / 100% |
| > 10 to <= 25 | Medium | 40 / 60 / 100% |
| > 25 | Large | 35 / 50 / 100% |

The report labels these deplaning presets as authored assumptions. The values remain editable. W15/W30/W60 are sizing-window shares, not passenger timestamps.

## Passenger tags

Schedule passengers carry:

- `FlightLegId`, `FlightLinkId`, `Airline`, `FlightNumber`
- `ScheduledDepartureTime` or `ScheduledArrivalTime`
- `CheckInOpenTime`, `CheckInLastAdmissionTime`
- `IsLateForCheckIn`, `ArrivedBeforeCheckInOpen`
- `DemandStepIndex`, `IsDesignStep`
- `ShowUpSignature`

The generator selects the busiest generated hour by default. Set `Design Hour` to a specific zero-based hour to override it.
