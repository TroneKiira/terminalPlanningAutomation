using AirportPlanning.Components;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.TerminalEntry;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    /// <summary>
    /// Terminal-entry admission queue. Departing passengers are split between
    /// manual verification and DigiYatra/e-gate pools, simulated with the same
    /// queue engine as the downstream facilities, then advanced to check-in.
    /// </summary>
    public class TerminalEntryComponent : GH_Component
    {
        private readonly TerminalEntrySolver _solver = new TerminalEntrySolver();

        public TerminalEntryComponent()
          : base(
                "Terminal Entry",
                "ENTRY",
                "Simulates the terminal-entry queue (manual verification and DigiYatra/e-gates) before check-in.",
                "Airport Planning",
                "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "Passenger population arriving at the terminal-entry checkpoint.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Service Profile", "SP",
                "Fallback service-time profile, used only when no Service Time Map is wired.",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddGenericParameter(
                "Service Time Map", "SP-MAP",
                "Optional per-passenger service-time map (Service Time Map). Supersedes Service Profile when wired. " +
                "Keys may use 'Manual Verification' and 'DigiYatra', or a profile/channel key.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;

            pManager.AddNumberParameter(
                "DigiYatra Share", "DY%",
                "Percentage of departing passengers routed to DigiYatra/e-gates. The rest use manual verification.",
                GH_ParamAccess.item, 37.5);

            pManager.AddNumberParameter(
                "Manual Positions", "MP",
                "Physical manual-verification positions. Used when Optimize Positions is off.",
                GH_ParamAccess.item, 3.0);

            pManager.AddNumberParameter(
                "DigiYatra Positions", "DP",
                "Physical DigiYatra/e-gate positions. Used when Optimize Positions is off.",
                GH_ParamAccess.item, 1.0);

            pManager.AddBooleanParameter(
                "Optimize Positions", "O",
                "Automatically size each entry pool until its Pool Plan target wait is met, inside the Resource Domain.",
                GH_ParamAccess.item, false);

            pManager.AddNumberParameter(
                "Staffing Percentage", "S%",
                "Percentage of physical positions actively staffed.",
                GH_ParamAccess.item, 100.0);

            pManager.AddNumberParameter(
                "Random Seed", "S",
                "Random number seed for service times and route allocation.",
                GH_ParamAccess.item, 1.0);

            pManager.AddIntervalParameter(
                "Resource Domain", "RD",
                "Inclusive fallback bounds for the optimizer's physical-position search when a Pool Plan supplies no range. Default 1 to 1200.",
                GH_ParamAccess.item,
                new Interval(1, ChannelResourcePlanResolver.DefaultMaxResources));
            pManager[9].Optional = true;

            pManager.AddGenericParameter(
                "Pool Plans", "PP",
                "Optional keyed pool plans. Use pool names 'Manual Verification' and 'DigiYatra'. " +
                "Each plan supplies the nominal positions, MQT and resource range for its pool.",
                GH_ParamAccess.list);
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after terminal entry, advanced toward check-in.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Terminal Entry Passengers", "EP", "Departing passengers served by the terminal-entry pools.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Complete terminal-entry queue simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Physical Positions", "P", "Total physical positions across both entry pools.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Active Positions", "A", "Total staffed positions simulated.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Peak Queue Length", "PQ", "Facility-wide maximum total queue length across both pools.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Average Wait", "AW", "Facility-wide average passenger waiting time (minutes), pooled across both entry routes.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Maximum Wait", "MW", "Facility-wide maximum individual passenger waiting time (minutes) across both pools.", GH_ParamAccess.item);
            pManager.AddNumberParameter("95th Percentile Wait", "W95", "Facility-wide whole-run 95th percentile passenger waiting time (minutes), pooled across both routes.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Utilization", "U", "Mean utilization across the simulated entry positions.", GH_ParamAccess.item);
            pManager.AddTextParameter("Pool Names", "PN", "Pool names in output order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Physical per Pool", "PPP", "Physical positions per pool, in output order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Active per Pool", "PAP", "Staffed positions per pool, in output order.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wait 95 per Pool", "WPP", "Whole-run 95th percentile wait per pool, in output order.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Search Outcomes", "SO", "Optimizer search outcomes per pool, including every (capacity, Wait95) point evaluated.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Nominal per Pool", "NPP", "Nominal physical positions per pool, before optimization.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Average Wait per Pool", "AWPP", "Average passenger waiting time (minutes) per pool, aligned with Pool Names.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Maximum Wait per Pool", "MWPP", "Maximum individual passenger waiting time (minutes) per pool, aligned with Pool Names.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Peak Queue per Pool", "PQPP", "Maximum queue length per pool, aligned with Pool Names.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            object rawServiceProfiles = null;
            double digiYatraShare = 37.5;
            double manualPositions = 3.0;
            double digiYatraPositions = 1.0;
            bool optimize = false;
            double staffingPercentage = 100.0;
            double seed = 1.0;
            Interval domain = Interval.Unset;
            List<object> rawPoolPlans = new List<object>();

            if (!DA.GetDataList(0, passengers)) return;
            DA.GetData(1, ref serviceProfile);
            DA.GetData(2, ref rawServiceProfiles);
            DA.GetData(3, ref digiYatraShare);
            DA.GetData(4, ref manualPositions);
            DA.GetData(5, ref digiYatraPositions);
            DA.GetData(6, ref optimize);
            DA.GetData(7, ref staffingPercentage);
            DA.GetData(8, ref seed);
            DA.GetData(9, ref domain);
            DA.GetDataList(10, rawPoolPlans);

            TerminalEntryInputs input = new TerminalEntryInputs
            {
                Passengers = passengers,
                ServiceProfile = serviceProfile,
                ServiceTimeProvider = ComponentDataHelpers.Unwrap<IServiceTimeProvider>(rawServiceProfiles),
                DigiYatraSharePercentage = digiYatraShare,
                ManualPhysicalPositions = Math.Max(1, (int)Math.Round(manualPositions)),
                DigiYatraPhysicalPositions = Math.Max(1, (int)Math.Round(digiYatraPositions)),
                OptimizeResources = optimize,
                StaffingPercentage = staffingPercentage,
                Seed = (int)Math.Round(seed),
                ResourceDomain = FacilityComponentInput.ToResourceDomain(domain),
                PoolPlans = UnwrapPoolPlans(rawPoolPlans)
            };

            TerminalEntryResults result = _solver.Solve(input);

            foreach (string warning in _solver.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            QueueResults queue = result.QueueResults;
            double utilization = queue.CounterUtilization != null && queue.CounterUtilization.Count > 0
                ? queue.CounterUtilization.Average()
                : 0.0;

            DA.SetDataList(0, queue.Passengers);
            DA.SetDataList(1, queue.ProcessedPassengers);
            DA.SetData(2, queue);
            DA.SetData(3, result.PhysicalPositions);
            DA.SetData(4, result.ActivePositions);
            DA.SetData(5, queue.PeakQueueLength);
            DA.SetData(6, queue.AverageWait);
            DA.SetData(7, queue.MaxWait);
            DA.SetData(8, queue.Wait95);
            DA.SetData(9, utilization);
            DA.SetDataList(10, result.PoolNames);
            DA.SetDataList(11, result.PhysicalPerPool);
            DA.SetDataList(12, result.ActivePerPool);
            DA.SetDataList(13, result.Wait95ListPerPool.Count == result.PoolNames.Count
                ? result.Wait95ListPerPool
                : FacilityComponentInput.GetWaitForPools(
                    result.Wait95PerPool, queue, result.PoolNames));
            DA.SetDataList(14, result.SearchOutcomes ?? new List<ResourceSearchOutcome>());
            DA.SetDataList(15, result.NominalPerPool);
            DA.SetDataList(16, result.AverageWaitPerPool);
            DA.SetDataList(17, result.MaxWaitPerPool);
            DA.SetDataList(18, result.PeakQueuePerPool);
        }

        private static List<PoolPlan> UnwrapPoolPlans(List<object> raw)
        {
            List<PoolPlan> plans = new List<PoolPlan>();

            if (raw == null)
                return plans;

            foreach (object item in raw)
            {
                PoolPlan plan = ComponentDataHelpers.Unwrap<PoolPlan>(item);
                if (plan != null)
                    plans.Add(plan);
            }

            return plans;
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("8B2F6A43-5C1D-4E7A-9B88-2F4D7C0E1A63"); }
        }
    }
}
