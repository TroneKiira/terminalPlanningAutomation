using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.Security;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class SecurityComponent : GH_Component
    {
        // Keep the solver alive permanently so it remembers its optimizer cache.
        private SecuritySolver _solver = new SecuritySolver();

        public SecurityComponent()
          : base("Security", "SEC", "Simulates passenger processing through security screening.", "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering security.", GH_ParamAccess.list);

            pManager.AddGenericParameter("Service Profile", "SP", "The security process time (one profile). In series mode this is the ATRS stage time; the e-gate stage uses 'E-Gate Service Time' or a 'EGate' key on Service Profiles.", GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddNumberParameter("Security Lanes", "L", "Physical ATRS screening lanes. Used when Optimize Lanes is off and no 'ATRS' pool plan is wired.", GH_ParamAccess.list, 8);

            pManager.AddBooleanParameter("Optimize Lanes", "O", "Automatically determine required lanes.", GH_ParamAccess.item, false);

            pManager.AddNumberParameter("Target Wait", "TW", "Target whole-run 95th percentile wait in minutes (warm-up excluded), applied to the lanes and, in series mode, to the ATRS stage. Optimization sizes capacity until the whole run meets it, or warns at the resource limit.", GH_ParamAccess.list, 10.0);

            pManager.AddNumberParameter("Staffing Percentage", "S%", "Percentage of physical lanes staffed.", GH_ParamAccess.item, 100.0);

            pManager.AddNumberParameter("Random Seed", "S", "Random number seed.", GH_ParamAccess.item, 1);

            pManager.AddGenericParameter("Service Profiles", "SP-MAP", "Optional per-passenger service-time map (Service Time Map). Supersedes Service Profile when wired. In series mode give the stages their own times with the keys 'EGate' and 'ATRS'.", GH_ParamAccess.item);
            pManager[7].Optional = true;

            pManager.AddGenericParameter("Pool Plans", "PP", "Optional keyed pool plans (Pool Plans component). In single-stage mode they define each pool's nominal resources, target wait and resource domain; in series mode use the identities 'EGate' and 'ATRS'.", GH_ParamAccess.list);
            pManager[8].Optional = true;

            pManager.AddBooleanParameter("Series E-Gate + ATRS", "SER", "Simulate the checkpoint as two queues in series: an entry e-gate stage (pool 'EGate') feeding an ATRS screening stage (pool 'ATRS'). Every passenger queues at the e-gate first, is served, then queues for a screening lane. An 'EGate' pool plan overrides the e-gate pins below.", GH_ParamAccess.item, false);

            pManager.AddNumberParameter("E-Gate Gates", "EG", "Physical entry e-gates (stage 1), used in series mode when no 'EGate' pool plan is wired.", GH_ParamAccess.item, 1.0);

            pManager.AddNumberParameter("E-Gate Target Wait", "ETW", "Target 95th percentile wait at the entry e-gates in minutes, used in series mode when no 'EGate' pool plan is wired. Zero means no target.", GH_ParamAccess.item, 2.0);

            pManager.AddNumberParameter("E-Gate Service Time", "ES", "Mean entry e-gate service time in seconds, used in series mode when the Service Profiles map has no 'EGate' key.", GH_ParamAccess.item, 7.0);

            pManager.AddNumberParameter("E-Gate to ATRS Walk", "EW", "Walk from the e-gate to the screening lane in minutes. Delays every arrival at the ATRS stage without being a service time.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after security.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Queue simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Physical Lanes", "L", "Total physical lanes across every pool (in series mode: e-gates plus screening lanes).", GH_ParamAccess.item);
            pManager.AddTextParameter("Pool Names", "PN", "Pool names in output order. Series mode lists 'EGate' then 'ATRS'; otherwise each dedicated channel, or 'Shared' for a single pool.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Physical per Pool", "LPP", "Physical lanes per pool, in output order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Active per Pool", "LAP", "Staffed lanes per pool, in output order.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wait 95 per Pool", "WPP", "Whole-run 95th percentile wait per pool, in output order.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Search Outcomes", "SO", "Optimizer search outcomes per pool, including every (capacity, Wait95) point evaluated. Feed an Optimizer Chart.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Nominal per Pool", "NPP", "Nominal (PNR) physical lanes per pool, before optimization.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            List<double> lanesDbl = new List<double>();
            bool optimize = false;
            List<double> targetWaits = new List<double>();
            double staffingPercentage = 100.0;
            double seedDbl = 1.0;
            object rawServiceProfiles = null;
            List<object> rawPoolPlans = new List<object>();
            bool series = false;
            double eGateGates = 1.0;
            double eGateTargetWait = 2.0;
            double eGateServiceSeconds = 7.0;
            double eGateWalk = 0.0;

            if (!DA.GetDataList(0, inPassengers)) return;
            DA.GetData(1, ref serviceProfile);
            DA.GetDataList(2, lanesDbl);
            DA.GetData(3, ref optimize);
            DA.GetDataList(4, targetWaits);
            DA.GetData(5, ref staffingPercentage);
            DA.GetData(6, ref seedDbl);
            DA.GetData(7, ref rawServiceProfiles);
            DA.GetDataList(8, rawPoolPlans);
            DA.GetData(9, ref series);
            DA.GetData(10, ref eGateGates);
            DA.GetData(11, ref eGateTargetWait);
            DA.GetData(12, ref eGateServiceSeconds);
            DA.GetData(13, ref eGateWalk);

            IServiceTimeProvider serviceTimeProvider =
                ComponentDataHelpers.Unwrap<IServiceTimeProvider>(rawServiceProfiles);
            List<PoolPlan> poolPlans = UnwrapPoolPlans(rawPoolPlans);

            // Per-stage e-gate values: a Pool Plan ('EGate' identity) or a
            // Service Time Map ('EGate' key) still wins; these pins only supply
            // the fallback that used to be unreachable from the canvas.
            SecurityInputs input = new SecurityInputs
            {
                Passengers = inPassengers,
                ServiceProfile = serviceProfile,
                ServiceTimeProvider = serviceTimeProvider,
                PhysicalResources = ToIntList(lanesDbl),
                OptimizeResources = optimize,
                TargetWait95 = targetWaits,
                PoolPlans = poolPlans,
                Seed = (int)Math.Round(seedDbl),
                StaffingPercentage = staffingPercentage,
                UseEGateSeries = series,
                EGatePhysicalResources = Math.Max(1, (int)Math.Round(eGateGates)),
                EGateTargetWait95 = Math.Max(0.0, eGateTargetWait),
                EGateServiceTimeSeconds = Math.Max(0.0, eGateServiceSeconds),
                EGateToAtrsWalkMinutes = Math.Max(0.0, eGateWalk)
            };

            SecurityResults finalResult = _solver.Solve(input);

            foreach (string warning in _solver.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            QueueResults results = finalResult.QueueResults;

            DA.SetDataList(0, results.Passengers);
            DA.SetData(1, results);
            DA.SetData(2, finalResult.PhysicalCounters);
            DA.SetDataList(3, finalResult.PoolNames);
            DA.SetDataList(4, finalResult.PhysicalPerChannel);
            DA.SetDataList(5, finalResult.ActivePerChannel);
            DA.SetDataList(6, FacilityComponentInput.GetWaitForPools(
                finalResult.Wait95PerChannel, results, finalResult.PoolNames));
            DA.SetDataList(7, finalResult.SearchOutcomes ?? new List<ResourceSearchOutcome>());
            DA.SetDataList(8, finalResult.NominalPerChannel);
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

        private static List<int> ToIntList(List<double> values)
        {
            List<int> result = new List<int>();

            if (values == null)
                return result;

            foreach (double value in values)
            {
                int rounded = (int)Math.Round(value);

                result.Add(rounded < 1 ? 1 : rounded);
            }

            return result;
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("5B8FD7FC-4F44-47EB-A30E-0DD0D1425FAA"); } }
    }
}
