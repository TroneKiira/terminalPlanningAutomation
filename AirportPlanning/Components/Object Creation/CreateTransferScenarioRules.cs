using System;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using Grasshopper.Kernel;

namespace AirportPlanning.Components.Object_Creation
{
    public class CreateTransferScenarioRules : GH_Component
    {
        public CreateTransferScenarioRules()
            : base(
                "Transfer Scenario Rules",
                "TSR",
                "Defines connection-time and processing assumptions for one transfer flow, " +
                "including where the flow clears security, immigration and emigration " +
                "(shared facility or dedicated transfer pool).",
                "Airport Planning",
                "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "Scenario", "S",
                "0 = Domestic→Domestic, 1 = Domestic→International, " +
                "2 = International→Domestic, 3 = International→International.",
                GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Minimum Connection", "MIN", "Minimum connection time in minutes.", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Maximum Connection", "MAX", "Maximum connection time in minutes.", GH_ParamAccess.item, 1440.0);
            pManager.AddBooleanParameter("Transfer Desk", "TDSK", "Transfer passengers present at a transfer desk (airline assistance / documentation) before continuing.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Security", "SEC", "Transfer passengers require security screening.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Immigration", "IMM", "Transfer passengers require immigration.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Emigration", "EMI", "Transfer passengers require emigration.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Baggage Recheck", "BAG", "Transfer passengers require baggage recheck.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Check-In", "CHK", "Transfer passengers require check-in.", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Bag Drop", "BD", "Transfer passengers require bag drop.", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Security Routing", "SRTE", "Where this flow clears security: 0 = Integrated (shared facility), 1 = Separate (dedicated transfer pool).", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Immigration Routing", "IRTE", "Where this flow clears immigration: 0 = Integrated (shared facility), 1 = Separate (dedicated transfer pool).", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Emigration Routing", "ERTE", "Where this flow clears emigration: 0 = Integrated (shared facility), 1 = Separate (dedicated transfer pool).", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Scenario Rules", "R", "Connection and process policy for the selected transfer flow.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int scenario = 0;
            double minimum = 0.0;
            double maximum = 1440.0;
            bool transferDesk = false;
            bool security = false;
            bool immigration = false;
            bool emigration = false;
            bool baggageRecheck = false;
            bool checkIn = false;
            bool bagDrop = false;
            int securityRouting = 0;
            int immigrationRouting = 0;
            int emigrationRouting = 0;

            if (!DA.GetData(0, ref scenario) ||
                !DA.GetData(1, ref minimum) ||
                !DA.GetData(2, ref maximum) ||
                !DA.GetData(3, ref transferDesk) ||
                !DA.GetData(4, ref security) ||
                !DA.GetData(5, ref immigration) ||
                !DA.GetData(6, ref emigration) ||
                !DA.GetData(7, ref baggageRecheck) ||
                !DA.GetData(8, ref checkIn) ||
                !DA.GetData(9, ref bagDrop) ||
                !DA.GetData(10, ref securityRouting) ||
                !DA.GetData(11, ref immigrationRouting) ||
                !DA.GetData(12, ref emigrationRouting))
                return;

            if (scenario < 0 || scenario > 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Scenario must be an integer from 0 to 3.");
                return;
            }

            if (securityRouting != (int)TransferFacilityRouting.Integrated &&
                securityRouting != (int)TransferFacilityRouting.Separate)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Security Routing must be 0 (Integrated) or 1 (Separate).");
                return;
            }

            if (immigrationRouting != (int)TransferFacilityRouting.Integrated &&
                immigrationRouting != (int)TransferFacilityRouting.Separate)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Immigration Routing must be 0 (Integrated) or 1 (Separate).");
                return;
            }

            if (emigrationRouting != (int)TransferFacilityRouting.Integrated &&
                emigrationRouting != (int)TransferFacilityRouting.Separate)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Emigration Routing must be 0 (Integrated) or 1 (Separate).");
                return;
            }

            TransferScenarioRules rules = new TransferScenarioRules
            {
                Scenario = (TransferScenario)scenario,
                HasProcessingRules = true,
                MinimumConnectionMinutes = minimum,
                MaximumConnectionMinutes = maximum,
                RequiresTransferDesk = transferDesk,
                RequiresSecurityScreening = security,
                RequiresImmigration = immigration,
                RequiresEmigration = emigration,
                RequiresBaggageRecheck = baggageRecheck,
                RequiresCheckIn = checkIn,
                RequiresBagDrop = bagDrop,
                SecurityRouting = (TransferFacilityRouting)securityRouting,
                ImmigrationRouting = (TransferFacilityRouting)immigrationRouting,
                EmigrationRouting = (TransferFacilityRouting)emigrationRouting
            };

            TransferDemandProfile validation = new TransferDemandProfile();
            validation.ScenarioRules[(TransferScenario)scenario] = rules;
            foreach (string error in validation.GetValidationErrors())
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);

            DA.SetData(0, rules);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("AA1F8B48-2F6B-4C42-91ED-16DF532D7AF1");
    }
}
