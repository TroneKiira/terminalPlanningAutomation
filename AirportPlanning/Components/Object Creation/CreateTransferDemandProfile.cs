using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Schedules;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// Builds the transfer-demand assumptions used by both PHP and Design Day
    /// generation. In Design Day mode, transfer shares apply to the full-day
    /// departure market and connected sliders are bounded by all-day schedule
    /// capacity and the scenario allocation limits.
    /// </summary>
    public class CreateTransferDemandProfile : GH_Component
    {
        private bool _rangeUpdateScheduled;

        public CreateTransferDemandProfile()
            : base(
                "Transfer Demand Profile",
                "TDP",
                "Defines Domestic/International transfer percentages, allocates them to four transfer flows, " +
                "and dynamically caps connected sliders when a Design Day schedule is supplied.",
                "Airport Planning",
                "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "Method", "M",
                "0 = PHP/aggregate method, 1 = Design Day schedule method.",
                GH_ParamAccess.item, 0);
            pManager.AddGenericParameter(
                "Flight Schedule", "FS",
                "Required for Method 1. Used to calculate the transfer-slider limits.",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddNumberParameter("Domestic Transfer %", "D%", "Share of all Domestic departing passengers across the schedule day assigned to transfers. In PHP/aggregate mode this is applied to the generated design-hour population.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("International Transfer %", "I%", "Share of all International departing passengers across the schedule day assigned to transfers. In PHP/aggregate mode this is applied to the generated design-hour population.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Domestic→Domestic %", "D-D%", "Allocation of the Domestic transfer total to Domestic→Domestic.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Domestic→International %", "D-I%", "Allocation of the International transfer total to Domestic→International.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("International→Domestic %", "I-D%", "Allocation of the Domestic transfer total to International→Domestic.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("International→International %", "I-I%", "Allocation of the International transfer total to International→International.", GH_ParamAccess.item, 0.0);
            pManager.AddIntegerParameter("Via Eligibility", "VE", "Which schedule legs may carry transfers: 0 = all passenger legs within the connection windows (default; a direct inbound flight still delivers connecting passengers), 1 = only via-qualified legs (Via differs from Origin/Destination).", GH_ParamAccess.item, 0);
            pManager.AddGenericParameter("Scenario Rules", "R", "Optional list of Transfer Scenario Rules objects. Unspecified flows use permissive 0–1440 minute windows and no process flags.", GH_ParamAccess.list);
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Transfer Demand Profile", "TDP", "Transfer assumptions to wire into a passenger generator.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Domestic Transfer Max %", "DMAX", "Maximum allowed Domestic transfer share of all daily Domestic departures.", GH_ParamAccess.item);
            pManager.AddNumberParameter("International Transfer Max %", "IMAX", "Maximum allowed International transfer share of all daily International departures.", GH_ParamAccess.item);
            pManager.AddNumberParameter("D→D Allocation Max %", "DDMAX", "Current allowed Domestic→Domestic allocation.", GH_ParamAccess.item);
            pManager.AddNumberParameter("D→I Allocation Max %", "DIMAX", "Current allowed Domestic→International allocation.", GH_ParamAccess.item);
            pManager.AddNumberParameter("I→D Allocation Max %", "IDMAX", "Current allowed International→Domestic allocation.", GH_ParamAccess.item);
            pManager.AddNumberParameter("I→I Allocation Max %", "IIMAX", "Current allowed International→International allocation.", GH_ParamAccess.item);
            pManager.AddTextParameter("Validation", "V", "Validation and schedule-cap messages.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int method = 0;
            FlightScheduleResult schedule = null;
            double domesticPercent = 0.0;
            double internationalPercent = 0.0;
            double domesticToDomestic = 0.0;
            double domesticToInternational = 0.0;
            double internationalToDomestic = 0.0;
            double internationalToInternational = 0.0;
            int viaEligibility = 0;
            List<TransferScenarioRules> scenarioRules = new List<TransferScenarioRules>();

            if (!DA.GetData(0, ref method) ||
                !DA.GetData(2, ref domesticPercent) ||
                !DA.GetData(3, ref internationalPercent) ||
                !DA.GetData(4, ref domesticToDomestic) ||
                !DA.GetData(5, ref domesticToInternational) ||
                !DA.GetData(6, ref internationalToDomestic) ||
                !DA.GetData(7, ref internationalToInternational))
                return;

            DA.GetData(1, ref schedule);
            DA.GetData(8, ref viaEligibility);
            DA.GetDataList(9, scenarioRules);

            if (method < 0 || method > 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Method must be 0 (PHP) or 1 (Design Day schedule).");
                return;
            }

            if (viaEligibility != (int)TransferScheduleEligibility.AllPassengerLegs &&
                viaEligibility != (int)TransferScheduleEligibility.ViaQualifiedLegs)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Via Eligibility must be 0 (all passenger legs) or 1 (via-qualified legs only).");
                return;
            }

            if (method == 1 && schedule == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Design Day method requires a Flight Schedule. Connect Flight Schedule Import's output.");
                return;
            }

            TransferDemandProfile profile = new TransferDemandProfile
            {
                ScheduleEligibility = (TransferScheduleEligibility)viaEligibility,
                DomesticTransferPercent = domesticPercent,
                InternationalTransferPercent = internationalPercent,
                DomesticToDomesticPercent = domesticToDomestic,
                DomesticToInternationalPercent = domesticToInternational,
                InternationalToDomesticPercent = internationalToDomestic,
                InternationalToInternationalPercent = internationalToInternational
            };

            if (scenarioRules.Count > 0)
            {
                foreach (TransferScenarioRules rule in scenarioRules.Where(r => r != null))
                    profile.ScenarioRules[rule.Scenario] = rule;
            }

            double[] maxima = { 100.0, 100.0, 100.0, 100.0, 100.0, 100.0 };
            List<string> messages = new List<string>();

            if (method == 1)
            {
                foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
                {
                    if (!scenarioRules.Any(rule => rule != null && rule.Scenario == scenario))
                    {
                        messages.Add(
                            $"No explicit connection window supplied for {scenario.ToFlowLabel()}; " +
                            "the permissive 0–1440 minute default is being used.");
                    }
                }
            }

            if (method == 1)
            {
                try
                {
                    TransferDemandProfile zeroed = ZeroShares(profile);
                    TransferDemandResult capacityProbe = TransferDemandCalculator.CalculateSchedule(schedule, zeroed);
                    TransferScheduleCapacity capacity = capacityProbe.Capacity;
                    maxima[0] = capacity.DomesticDepartureShareCap(capacityProbe.DomesticDepartureTotal);
                    maxima[1] = capacity.InternationalDepartureShareCap(capacityProbe.InternationalDepartureTotal);

                    int requestedDomesticTransferTotal =
                        PercentageOf(capacityProbe.DomesticDepartureTotal, domesticPercent);
                    int requestedInternationalTransferTotal =
                        PercentageOf(capacityProbe.InternationalDepartureTotal, internationalPercent);

                    if (capacityProbe.DomesticDepartureTotal > 0 ||
                        capacityProbe.InternationalDepartureTotal > 0)
                    {
                        messages.Add(
                            $"Design Day basis: {capacityProbe.DomesticDepartureTotal} Domestic and " +
                            $"{capacityProbe.InternationalDepartureTotal} International daily departures; " +
                            $"the entered shares request {requestedDomesticTransferTotal + requestedInternationalTransferTotal} " +
                            "outbound transfers across the day.");
                    }

                    if (requestedDomesticTransferTotal > capacity.DomesticOutbound)
                    {
                        messages.Add(
                            $"Requested Domestic outbound transfers ({requestedDomesticTransferTotal}) exceed the " +
                            $"schedule-feasible total ({capacity.DomesticOutbound}). The Domestic share will " +
                            "be capped; no entered percentage is changed silently.");
                    }

                    if (requestedInternationalTransferTotal > capacity.InternationalOutbound)
                    {
                        messages.Add(
                            $"Requested International outbound transfers ({requestedInternationalTransferTotal}) exceed " +
                            $"the schedule-feasible total ({capacity.InternationalOutbound}). The International " +
                            "share will be capped; no entered percentage is changed silently.");
                    }

                    // Scenario allocation limits are based on the demand that
                    // survives the market-share cap, not on an uncapped entry.
                    int domesticTransferTotal = PercentageOf(
                        capacityProbe.DomesticDepartureTotal,
                        Math.Min(domesticPercent, maxima[0]));
                    int internationalTransferTotal = PercentageOf(
                        capacityProbe.InternationalDepartureTotal,
                        Math.Min(internationalPercent, maxima[1]));

                    int dToDCount = PercentageOf(domesticTransferTotal, domesticToDomestic);
                    int dToICount = PercentageOf(internationalTransferTotal, domesticToInternational);
                    int iToDCount = PercentageOf(domesticTransferTotal, internationalToDomestic);
                    int iToICount = PercentageOf(internationalTransferTotal, internationalToInternational);
                    int allOtherFlows = dToDCount + dToICount + iToDCount + iToICount;

                    maxima[2] = AllocationMaximum(
                        domesticTransferTotal,
                        capacity.ByScenario[TransferScenario.DomesticToDomestic],
                        Math.Min(
                            Math.Min(capacity.DomesticOutbound - iToDCount,
                                     capacity.DomesticInbound - dToICount),
                            capacity.Total - (allOtherFlows - dToDCount)));
                    maxima[4] = AllocationMaximum(
                        domesticTransferTotal,
                        capacity.ByScenario[TransferScenario.InternationalToDomestic],
                        Math.Min(
                            Math.Min(capacity.DomesticOutbound - dToDCount,
                                     capacity.InternationalInbound - iToICount),
                            capacity.Total - (allOtherFlows - iToDCount)));
                    maxima[3] = AllocationMaximum(
                        internationalTransferTotal,
                        capacity.ByScenario[TransferScenario.DomesticToInternational],
                        Math.Min(
                            Math.Min(capacity.InternationalOutbound - iToICount,
                                     capacity.DomesticInbound - dToDCount),
                            capacity.Total - (allOtherFlows - dToICount)));
                    maxima[5] = AllocationMaximum(
                        internationalTransferTotal,
                        capacity.ByScenario[TransferScenario.InternationalToInternational],
                        Math.Min(
                            Math.Min(capacity.InternationalOutbound - dToICount,
                                     capacity.InternationalInbound - iToDCount),
                            capacity.Total - (allOtherFlows - iToICount)));

                    messages.AddRange(capacityProbe.Warnings);

                    if (capacity.Total == 0)
                    {
                        messages.Add(
                            profile.ScheduleEligibility == TransferScheduleEligibility.ViaQualifiedLegs
                                ? "No via-qualified transfer connections were found in the schedule. " +
                                  "Set Via Eligibility to 0 (all passenger legs) to also match direct " +
                                  "arrival/departure pairs inside the connection windows."
                                : "No feasible transfer connections were found inside the configured " +
                                  "connection windows. Check the scenario minimum/maximum connection times.");
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not calculate schedule transfer limits: " + ex.Message);
                }
            }

            double[] values =
            {
                domesticPercent,
                internationalPercent,
                domesticToDomestic,
                domesticToInternational,
                internationalToDomestic,
                internationalToInternational
            };

            for (int i = 0; i < values.Length; i++)
            {
                double capped = Math.Max(0.0, Math.Min(values[i], maxima[i]));
                if (Math.Abs(capped - values[i]) > 1e-9)
                {
                    string message = $"Transfer share input {i + 1} was capped from {values[i]:0.##}% to {capped:0.##}% by the selected method/schedule limits.";
                    messages.Add(message);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
                }

                values[i] = capped;
            }

            // Each pair allocates one outbound transfer total. Preserve the
            // first scenario input and reduce its sibling to the remaining
            // allocation if the pair exceeds 100%.
            double domesticAllocationBeforeCap = values[4];
            double internationalAllocationBeforeCap = values[5];
            values[4] = Math.Min(values[4], Math.Max(0.0, 100.0 - values[2]));
            values[5] = Math.Min(values[5], Math.Max(0.0, 100.0 - values[3]));

            if (values[4] < domesticAllocationBeforeCap)
                messages.Add("International→Domestic allocation was reduced to keep the Domestic outbound allocation at or below 100%.");
            if (values[5] < internationalAllocationBeforeCap)
                messages.Add("International→International allocation was reduced to keep the International outbound allocation at or below 100%.");

            maxima[2] = Math.Min(maxima[2], Math.Max(0.0, 100.0 - values[4]));
            maxima[4] = Math.Min(maxima[4], Math.Max(0.0, 100.0 - values[2]));
            maxima[3] = Math.Min(maxima[3], Math.Max(0.0, 100.0 - values[5]));
            maxima[5] = Math.Min(maxima[5], Math.Max(0.0, 100.0 - values[3]));

            profile.DomesticTransferPercent = values[0];
            profile.InternationalTransferPercent = values[1];
            profile.DomesticToDomesticPercent = values[2];
            profile.DomesticToInternationalPercent = values[3];
            profile.InternationalToDomesticPercent = values[4];
            profile.InternationalToInternationalPercent = values[5];

            bool changed = false;
            for (int i = 0; i < maxima.Length; i++)
                changed |= UpdateConnectedSliders(i + 2, maxima[i]);

            if (changed && !_rangeUpdateScheduled)
            {
                _rangeUpdateScheduled = true;
                GH_Document document = OnPingDocument();
                if (document != null)
                {
                    document.ScheduleSolution(1, doc =>
                    {
                        _rangeUpdateScheduled = false;
                        ExpireSolution(false);
                    });
                }
                else
                {
                    _rangeUpdateScheduled = false;
                }
            }

            foreach (string error in profile.GetValidationErrors())
            {
                messages.Add(error);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);
            }

            DA.SetData(0, profile);
            for (int i = 0; i < maxima.Length; i++)
                DA.SetData(i + 1, maxima[i]);
            DA.SetDataList(7, messages.Distinct());
        }

        private bool UpdateConnectedSliders(int inputIndex, double maximum)
        {
            if (inputIndex < 0 || inputIndex >= Params.Input.Count)
                return false;

            decimal max = (decimal)Math.Max(0.0, Math.Min(100.0, maximum));
            bool changed = false;

            foreach (IGH_Param source in Params.Input[inputIndex].Sources)
            {
                GH_NumberSlider slider = source as GH_NumberSlider;
                if (slider == null || slider.Slider == null)
                    continue;

                if (slider.Recipients.Count != 1 ||
                    slider.Recipients[0].InstanceGuid != Params.Input[inputIndex].InstanceGuid)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        $"Slider connected to {Params.Input[inputIndex].Name} is also used elsewhere; " +
                        "its range was not changed to avoid affecting other components.");
                    continue;
                }

                if (slider.Slider.Minimum == 0m && slider.Slider.Maximum == max)
                    continue;

                slider.Slider.Type = GH_SliderAccuracy.Float;
                slider.Slider.DecimalPlaces = 2;
                slider.Slider.Minimum = 0m;
                slider.Slider.Maximum = max;
                if (slider.CurrentValue > max)
                    slider.SetSliderValue(max);
                else if (slider.CurrentValue < 0m)
                    slider.SetSliderValue(0m);

                slider.ExpireSolution(false);
                changed = true;
            }

            return changed;
        }

        private static double AllocationMaximum(
            int parentTransferTotal,
            int scheduleFlowMaximum,
            int sharedScheduleResidual)
        {
            if (parentTransferTotal <= 0)
                return 100.0;

            int feasible = Math.Max(0, Math.Min(scheduleFlowMaximum, sharedScheduleResidual));
            double schedulePercent = feasible * 100.0 / parentTransferTotal;
            return Math.Max(0.0, Math.Min(100.0, schedulePercent));
        }

        private static int PercentageOf(int count, double percent)
        {
            if (count <= 0 || percent <= 0.0 || double.IsNaN(percent) || double.IsInfinity(percent))
                return 0;

            return (int)Math.Floor(count * Math.Min(100.0, percent) / 100.0 + 1e-9);
        }

        private static TransferDemandProfile ZeroShares(TransferDemandProfile profile)
        {
            TransferDemandProfile clone = new TransferDemandProfile
            {
                ScheduleEligibility = profile.ScheduleEligibility,
                DomesticTransferPercent = 0.0,
                InternationalTransferPercent = 0.0,
                DomesticToDomesticPercent = 0.0,
                DomesticToInternationalPercent = 0.0,
                InternationalToDomesticPercent = 0.0,
                InternationalToInternationalPercent = 0.0,
                ScenarioRules = new Dictionary<TransferScenario, TransferScenarioRules>()
            };

            foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
                clone.ScenarioRules[scenario] = profile.RulesFor(scenario).Clone();

            return clone;
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("F8C4D37B-80DF-41FC-9702-B44E0C741A31");
    }
}
