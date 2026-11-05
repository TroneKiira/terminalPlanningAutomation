# Transfer passenger generation

Transfer demand can be supplied to the PHP/aggregate generators and to the
Design Day Schedule Passenger Generator with the same `Transfer Demand Profile`.

## Grasshopper wiring

1. Create one `Transfer Scenario Rules` object for each flow you want to
   configure. Scenario IDs are:
   - `0`: Domestic → Domestic
   - `1`: Domestic → International
   - `2`: International → Domestic
   - `3`: International → International
2. Wire those objects into `Transfer Demand Profile` → `Scenario Rules`.
3. Connect six Number Sliders to the percentage inputs:
   - Domestic and International transfer percentages.
   - The four scenario allocation percentages.
4. Wire the profile output to the `TDP` input on `Passenger Generator`,
   `Windowed Passenger Generator`, or `Schedule Passenger Generator`.

For PHP/aggregate mode, set `Method` to `0`; no schedule is needed. For Design
Day, set `Method` to `1` and wire the same imported flight schedule to both the
profile's `Flight Schedule` input and the Schedule Passenger Generator.

The six sliders should be dedicated to these inputs. The profile component
updates the connected sliders' visible limits. A shared slider is left unchanged
to avoid changing its range for other parts of the Grasshopper definition.

## Share and cap rules

In Design Day mode, the Domestic and International transfer percentages are
shares of **all departing passengers across the operating day**, not of the
busiest hour. The schedule's rolling departure peak (and the separate transfer
departure/arrival peaks) are still reported, but they do not change the
requested daily transfer count.

For example, 25,000 Domestic and 15,000 International daily departures at 10%
each request 2,500 + 1,500 = 4,000 outbound transfers. Peak-hour transfer
figures are reported alongside that full-day total.

PHP/aggregate mode applies the same percentages to the generated design-hour
population because that population is already the peak cohort.

The four scenario percentages allocate those transfer totals:

- Domestic → Domestic + International → Domestic ≤ 100% of Domestic transfers.
- Domestic → International + International → International ≤ 100% of
  International transfers.

PHP uses the entered shares without a schedule-based cap. Design Day limits the
shares to what the full-day schedule can support. A leg is via-qualified when
its Via station differs from its Origin (arrival) or Destination (departure). A
candidate connection must also fall within that scenario's configured minimum
and maximum connection times. Schedule passenger counts are treated as
capacities; they do not mean every passenger on a via-qualified flight
transfers.

The schedule calculation allocates count-level transfers to feasible arrival
and departure legs. It does not create a persistent identity linking one
generated arrival passenger to one generated departure passenger.

When the schedule cannot satisfy the entered demand, the Validation output and
the generator warnings show the requested and effective counts. The planner's
entered percentages are never changed without a warning naming the cap.

## Leg eligibility (Via filter)

The `Via Eligibility` input on `Transfer Demand Profile` decides which
schedule legs may carry transfer demand in Design Day mode:

- `0` **All passenger legs** (default). Matching runs on sectors, scenario
  connection windows and per-leg passenger capacities. A direct inbound flight
  still delivers connecting passengers, so Via is not required — this matters
  for workbooks whose Arrival (via) column simply repeats the Origin.
- `1` **Via-qualified legs only.** A leg qualifies only when its Via differs
  from its Origin (arrivals) or Destination (departures) — the legacy
  data-quality restriction.

With Method 1, if the schedule yields zero connections under Via-qualified
eligibility, the Validation output suggests switching to all-passenger-legs.
The same eligibility drives both the connected-slider caps and the passenger
tagging, so the two can never disagree.

## Terminal entry

Transfer passengers are already airside and do not enter through the public
terminal hall. The `Terminal Entry` component therefore serves only originating
departures: transfer departures bypass the entry pools, keep their generated
arrival times, and continue on the component's `Passengers` output to downstream
facilities. The `Terminal Entry Passengers` output excludes them, and the
`Transfers Bypassing Entry` output reports how many were passed through.

Terminal-entry positions and the DigiYatra route split are sized from
originating departures only. Transfer demand never inflates entry capacity or
the DigiYatra share denominator.

## Processing rules

Scenario rules are copied to tagged passengers. The current check-in and
bag-drop requirements, security screening, and emigration processing honor
configured transfer rules. Immigration is honoured by the `Immigration`
simulation component. Baggage recheck and the transfer desk are honoured by
the `Baggage Recheck` and `Transfer Desk` components: the desk serves
arrival-leg transfer flows whose rules require it, and the recheck counter is
the desk facility for the International→Domestic flow — a flow whose rules set
both flags is queued at recheck only, and the desk component warns about it.

Every downstream solver applies the same service-use predicate that produces
the reported usage figures: a rule-based bypass is removed from that facility's
pool filter before queueing and capacity search, so it cannot appear in served
counts, wait percentiles or resource sizing. Transfers without explicit rules
keep normal passenger behaviour and are reported separately as unspecified.

### Required vs bypassed services

The Schedule Passenger Generator, Passenger Summary and Passenger Details report
per-service transfer usage:

- **Required** — the passenger carries explicit scenario rules and the rule
  requires that service.
- **Bypassed by scenario rule** — the passenger carries explicit scenario rules
  and the rule says the service is not required.
- **Without explicit transfer rules** — the passenger was tagged as a transfer
  but keeps normal passenger behaviour. This is *not* counted as a bypass.

A bypass only counts passengers who were actually tagged. It does not include a
flight leg whose Passenger Group/profile was missing, and it does not include
passengers who could not be served because of facility capacity. Those cases
are reported separately:

- A missing generated cohort produces a warning such as
  `Transfer demand could only tag 100 of 200 schedule-effective outbound
  passengers`.
- A facility capacity shortfall is reported by that facility's solver.

Daily and peak-hour usage are reported separately. Departure services
(check-in, bag drop, security, emigration) use the transfer departure peak
windows; arrival services (transfer desk, immigration, baggage recheck) use the
transfer arrival peak windows. PHP/aggregate populations are treated as one
peak cohort.

## Routing choices (shared vs dedicated pools)

Besides *whether* a flow clears a process, each scenario rule records *where*
it clears it. Three routing choices exist per flow — Security, Immigration and
Emigration — each either:

- `0` **Integrated** (default): the cohort joins the same facility pool as
  originating passengers.
- `1` **Separate**: the cohort is routed to a dedicated transfer pool for that
  facility's solve, keyed by the solver's transfer channel
  (`Transfer Security`, `Transfer Immigration`, `Transfer Emigration` by
  default). The upstream check-in channel is restored afterwards, so other
  facilities are unaffected.

To size a dedicated pool, add the transfer channel to the facility component's
`Channels` input and wire a matching Pool Plan. When Separate is requested but
no matching pool exists, the passengers fall back to the shared facility and
the solver raises a warning naming the pool that served them.

Separately-routed emigration transfers are also excluded from the
e-gate/counter route split; the e-gate share applies to the remaining
originating population only.

A **Transfer Desk** requirement flag is available per flow (input `TDSK` on
Transfer Scenario Rules). It tags the cohort for a transfer-desk process and is
reported by Passenger Details and Passenger Summary. The `Transfer Desk`
component (`T-DESK`) queues arrival-leg transfers whose rules require it; the
`Baggage Recheck` component (`T-RECHK`) does the same for the recheck flag and
is the TS-mapping desk facility for International→Domestic. Both take the full
population, serve only rule-required transfer arrivals, and return the full
population with served passengers advanced, so the PAX objects rejoin the
integrated arrival flow (or their Separate-routed pools) unchanged. Pool
identities are temporary: the components re-channel served passengers to
per-flow pools named "Transfer Desk (flow)" or "Baggage Recheck (flow)", and
restore the original channel before output. Key the Pool Plans by those names.
Split Pools per Flow off runs one shared queue instead.

`Passenger Details` and `Passenger Summary` expose the desk flag and each
Separate-routing choice per passenger, so definitions can be filtered or
counted downstream. `Passenger Summary` also reports the total scenario-rule
bypass count and a per-service usage list. `Passenger Details` reports whether
transfer rules were applied, the services bypassed by the scenario rule, and
the bypass reason for each passenger.
