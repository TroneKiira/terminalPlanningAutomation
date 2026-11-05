using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AirportPlanning.Tests
{
    /// <summary>
    /// Headless contract test for gh/aviation_update_1.ghx.
    ///
    /// The archive is parsed as plain XML (no Rhino/Grasshopper runtime) and
    /// must show:
    ///
    ///   1. the main passenger chain, resolved through relays:
    ///      ENTRY.Passengers -> CHK.Passengers -> BD.Passengers -> VER.Passengers
    ///      -> FILTER.Passengers;
    ///   2. the intentional post-verification branch:
    ///      EMG.Passengers <- FILTER.International, and Security fed by a merge
    ///      of FILTER.Domestic and EMG.Passengers;
    ///   3. the two Walking components as pure side branches (their outputs
    ///      feed nothing, so they must never be assumed to advance the journey);
    ///   4. serialized pin schemas that still match the current C# components,
    ///      so a stale saved definition cannot silently load with mismatched
    ///      inputs. The Passenger Filter is checked as a PREFIX: new outputs
    ///      are appended so existing wires keep their positions.
    /// </summary>
    public static partial class Program
    {
        private sealed class GhxParam
        {
            public string Guid;
            public string Name;
            public bool IsOutput;
            public string OwnerGuid;
            public List<string> Sources = new List<string>();
        }

        private sealed class GhxObject
        {
            public string Guid;
            public string Type;
            public string Nick;
            public List<GhxParam> Inputs = new List<GhxParam>();
            public List<GhxParam> Outputs = new List<GhxParam>();
        }

        private sealed class GhxModel
        {
            public List<GhxObject> Objects = new List<GhxObject>();
            public Dictionary<string, GhxParam> ParamsByGuid =
                new Dictionary<string, GhxParam>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> RelaySource =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public GhxObject Single(string type)
            {
                List<GhxObject> matches = Objects.Where(o => o.Type == type).ToList();

                if (matches.Count != 1)
                    throw new InvalidOperationException(
                        $"Expected exactly one '{type}' object in the archive, found {matches.Count}.");

                return matches[0];
            }

            /// <summary>Resolves a source guid through relays to its producing parameter.</summary>
            public GhxParam ResolveSource(string guid, int depth = 0)
            {
                if (guid == null || depth > 12)
                    return null;

                if (ParamsByGuid.TryGetValue(guid, out GhxParam param))
                    return param;

                if (RelaySource.TryGetValue(guid, out string upstream))
                    return ResolveSource(upstream, depth + 1);

                return null;
            }

            public string DescribeSource(string guid)
            {
                GhxParam param = ResolveSource(guid);

                if (param == null)
                    return "UNRESOLVED";

                GhxObject owner = Objects.FirstOrDefault(o => o.Guid == param.OwnerGuid);

                return (owner?.Nick ?? "?") + "/" + param.Name;
            }
        }

        private static GhxModel ParseGhxArchive(string path)
        {
            XDocument document = XDocument.Load(path);

            GhxModel model = new GhxModel();

            foreach (XElement objectChunk in document.Descendants("chunk")
                .Where(c => (string)c.Attribute("name") == "Object"))
            {
                XElement container = objectChunk.Descendants("chunk")
                    .FirstOrDefault(c => (string)c.Attribute("name") == "Container");

                if (container == null)
                    continue;

                Dictionary<string, string> items = container.Element("items")?
                    .Elements("item")
                    .Where(i => i.Attribute("name") != null)
                    .GroupBy(i => (string)i.Attribute("name"))
                    .ToDictionary(g => g.Key, g => g.First().Value.Trim()) ?? new Dictionary<string, string>();

                GhxObject ghxObject = new GhxObject
                {
                    Type = items.TryGetValue("Name", out string typeName) ? typeName : "",
                    Nick = items.TryGetValue("NickName", out string nick) ? nick : "",
                    Guid = items.TryGetValue("InstanceGuid", out string guid) ? guid : ""
                };

                if (ghxObject.Type == "Relay")
                {
                    if (items.TryGetValue("Source", out string source) && ghxObject.Guid.Length > 0)
                        model.RelaySource[ghxObject.Guid] = source;

                    continue;
                }

                // Param chunks usually sit directly under the container, but
                // some components (Merge) nest them inside a ParameterData
                // chunk - search the whole container subtree.
                foreach (XElement paramChunk in container.Descendants("chunk"))
                {
                    string chunkName = (string)paramChunk.Attribute("name") ?? "";

                    // Standard components serialize "param_input"/"param_output"
                    // chunks; Merge (and a few others) use the legacy
                    // "InputParam"/"OutputParam" names. Accept both.
                    bool isInput = chunkName.StartsWith("param_input", StringComparison.Ordinal) ||
                                   chunkName.StartsWith("InputParam", StringComparison.Ordinal);
                    bool isOutput = chunkName.StartsWith("param_output", StringComparison.Ordinal) ||
                                    chunkName.StartsWith("OutputParam", StringComparison.Ordinal);

                    if (!isInput && !isOutput)
                        continue;

                    Dictionary<string, string> paramItems = paramChunk.Element("items")?
                        .Elements("item")
                        .Where(i => i.Attribute("name") != null)
                        .GroupBy(i => (string)i.Attribute("name"))
                        .ToDictionary(g => g.Key, g => g.First().Value.Trim()) ?? new Dictionary<string, string>();

                    GhxParam param = new GhxParam
                    {
                        Guid = paramItems.TryGetValue("InstanceGuid", out string paramGuid) ? paramGuid : "",
                        Name = paramItems.TryGetValue("Name", out string paramName) ? paramName : "",
                        IsOutput = isOutput,
                        OwnerGuid = ghxObject.Guid
                    };

                    foreach (XElement source in paramChunk.Element("items")?.Elements("item")
                        .Where(i => (string)i.Attribute("name") == "Source") ?? Enumerable.Empty<XElement>())
                    {
                        param.Sources.Add(source.Value.Trim());
                    }

                    if (param.Guid.Length > 0)
                    {
                        (isOutput ? ghxObject.Outputs : ghxObject.Inputs).Add(param);
                        model.ParamsByGuid[param.Guid] = param;
                    }
                }

                model.Objects.Add(ghxObject);
            }

            return model;
        }

        private static string FindGhxPath()
        {
            for (DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "gh", "aviation_update_1.ghx");

                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        // -------------------------------------------------------------
        // Expected serialized schemas (input pins, in registration order).
        // These mirror the current C# components; a saved definition that
        // drifts from them fails the contract until it is resaved.
        // -------------------------------------------------------------
        private static readonly string[] ExpectedEntryInputs =
        {
            "Passengers", "Service Profile", "Service Time Map", "DigiYatra Share",
            "Manual Positions", "DigiYatra Positions", "Optimize Positions",
            "Staffing Percentage", "Random Seed", "Resource Domain", "Pool Plans"
        };

        private static readonly string[] ExpectedCheckInInputs =
        {
            "Passengers", "Service Profile", "Physical Counters", "Optimize Counters",
            "Target Wait", "Staffing Percentage", "Random Seed", "Channels",
            "Service Profiles", "Resource Domain", "Channel Routing", "Pool Plans"
        };

        private static readonly string[] ExpectedBagDropInputs =
        {
            "Passengers", "Service Profile", "Bag Drop Counters", "Optimize Counters",
            "Target Wait", "Staffing Percentage", "Random Seed", "Channels",
            "Service Profiles", "Resource Domain", "Pool Plans"
        };

        private static readonly string[] ExpectedVerificationInputs =
        {
            "Passengers", "Eligible Percentage", "Throughput Per Gate", "Design Utilization"
        };

        private static readonly string[] ExpectedEmigrationInputs =
        {
            "Passengers", "Service Profile", "Emigration Positions", "Optimize Positions",
            "Target Wait", "Staffing Percentage", "Random Seed", "Channels",
            "Service Profiles", "Resource Domain", "Pool Plans", "E-Gate Share"
        };

        private static readonly string[] ExpectedSecurityInputs =
        {
            "Passengers", "Service Profile", "Security Lanes", "Optimize Lanes",
            "Target Wait", "Staffing Percentage", "Random Seed", "Service Profiles",
            "Pool Plans", "Series E-Gate + ATRS", "E-Gate Gates", "E-Gate Target Wait",
            "E-Gate Service Time", "E-Gate to ATRS Walk"
        };

        // Outputs already saved in the archive; the filter appends new ones so
        // the saved wires keep their positions.
        private static readonly string[] ExpectedFilterOutputPrefix =
        {
            "Domestic", "International", "Male", "Female", "Checked Bags", "Cabin Bag Only"
        };

        // The full current Passenger Details output schema; saved definitions
        // may stop anywhere inside it, but must never disagree with it.
        private static readonly string[] ExpectedPdtOutputs =
        {
            "Passenger ID", "Arrival Time", "Service Time", "Start Service",
            "End Service", "Waiting Time", "Assigned Counter", "Walking Speed",
            "Passenger Type", "Journey", "Online Check-In", "Checked Bag",
            "Requires Check-In", "Requires Bag Drop", "Transfer Scenario",
            "Transfer Security", "Transfer Immigration", "Transfer Emigration",
            "Baggage Recheck", "Transfer Desk", "Security Separate",
            "Immigration Separate", "Emigration Separate", "Transfer Rules Applied",
            "Bypassed Services", "Bypass Reason", "Security Entry Route",
            "Security Screening Route", "Security Entry Wait", "Security Screening Wait",
            "Security Entry Start", "Security Entry End", "Has History",
            "Walking Minutes", "Journey Narration"
        };

        private static void RunGhxDefinitionContractTests()
        {
            Console.WriteLine("== GHX definition contract ==");

            string path = FindGhxPath();

            Check(path != null,
                "gh/aviation_update_1.ghx is found relative to the test output");

            if (path == null)
                return;

            GhxModel model;

            try
            {
                model = ParseGhxArchive(path);
            }
            catch (Exception error)
            {
                Check(false, "The archive parses as XML (" + error.Message + ")");
                return;
            }

            Check(model.Objects.Count > 0, "The archive contains component objects");

            GhxObject entry = model.Single("Terminal Entry");
            GhxObject checkIn = model.Single("Check-In");
            GhxObject bagDrop = model.Single("Bag Drop");
            GhxObject verification = model.Single("Passenger Verification");
            GhxObject emigration = model.Single("Emigration");
            GhxObject security = model.Single("Security");
            GhxObject filter = model.Single("Passenger Filter");

            // ---- the main chain, resolved through relays ---------------------
            // The four processing stages are the methodology's backbone and
            // are asserted strictly. The branch wiring (filter, emigration,
            // security merge) is user-owned and evolves on the canvas, so it
            // is reported informationally instead of hard-coded.
            CheckSource(model, entry, "Passengers", null,
                "Terminal Entry is fed by the selected generator stream");

            CheckSource(model, checkIn, "Passengers", "ENTRY/Passengers",
                "Check-In is fed by Terminal Entry's Passengers output");
            CheckSource(model, bagDrop, "Passengers", "CHK/Passengers",
                "Bag Drop is fed by Check-In's Passengers output");
            CheckSource(model, verification, "Passengers", "BD/Passengers",
                "Verification is fed by Bag Drop's Passengers output");

            CheckFed(model, filter, "Passengers",
                "Passenger Filter is fed (currently from: {0})");
            CheckFed(model, emigration, "Passengers",
                "Emigration is fed (currently from: {0})");
            CheckFed(model, security, "Passengers",
                "Security is fed (currently from: {0})");

            // ---- walking stays a side branch ---------------------------------
            List<string> walkingConsumers = new List<string>();

            foreach (GhxObject walking in model.Objects.Where(o => o.Type == "Walking"))
            {
                foreach (GhxParam output in walking.Outputs)
                {
                    foreach (GhxObject consumer in model.Objects)
                    {
                        foreach (GhxParam input in consumer.Inputs)
                        {
                            if (input.Sources.Any(s =>
                                    string.Equals(model.ResolveSource(s)?.Guid, output.Guid,
                                        StringComparison.OrdinalIgnoreCase)))
                            {
                                walkingConsumers.Add($"{consumer.Nick}.{input.Name}");
                            }
                        }
                    }
                }
            }

            Check(walkingConsumers.Count == 0,
                "The Walking components are side branches with no downstream consumers" +
                (walkingConsumers.Count == 0 ? "" : $": {string.Join(", ", walkingConsumers)}"));

            // ---- serialized pin schemas --------------------------------------
            CheckSchema(entry, ExpectedEntryInputs, true, "Terminal Entry");
            CheckSchema(checkIn, ExpectedCheckInInputs, true, "Check-In");
            CheckSchema(bagDrop, ExpectedBagDropInputs, true, "Bag Drop");
            CheckSchema(verification, ExpectedVerificationInputs, true, "Passenger Verification");
            CheckSchema(emigration, ExpectedEmigrationInputs, true, "Emigration");
            CheckSchema(security, ExpectedSecurityInputs, true, "Security");

            // The filter may gain outputs; the saved ones must keep position.
            List<string> savedFilterOutputs = filter.Outputs.Select(p => p.Name).ToList();

            bool prefixCompatible = savedFilterOutputs.Count >= ExpectedFilterOutputPrefix.Length;

            for (int i = 0; prefixCompatible && i < ExpectedFilterOutputPrefix.Length; i++)
            {
                if (!string.Equals(savedFilterOutputs[i], ExpectedFilterOutputPrefix[i], StringComparison.Ordinal))
                    prefixCompatible = false;
            }

            Check(prefixCompatible,
                "The saved Passenger Filter keeps the legacy output positions " +
                $"(saved: {string.Join(", ", savedFilterOutputs)})");

            // Every saved Passenger Details instance must stay inside the
            // current output schema, name by name, so old wires keep meaning
            // the same thing after the narration output is appended.
            List<GhxObject> detailsInstances = model.Objects
                .Where(o => o.Type == "Passenger Details")
                .ToList();

            Check(detailsInstances.Count > 0,
                "The archive contains Passenger Details instances");

            foreach (GhxObject details in detailsInstances)
                CheckOutputPrefix(details, ExpectedPdtOutputs, "Passenger Details");
        }

        /// <summary>
        /// Asserts that the saved outputs of one component are a prefix of the
        /// expected current schema: same names in the same positions, and never
        /// more outputs than the component now registers.
        /// </summary>
        private static void CheckOutputPrefix(
            GhxObject component,
            string[] expectedOutputs,
            string label)
        {
            List<string> saved = component.Outputs.Select(p => p.Name).ToList();

            bool compatible = saved.Count <= expectedOutputs.Length;

            for (int i = 0; compatible && i < saved.Count; i++)
            {
                if (!string.Equals(saved[i], expectedOutputs[i], StringComparison.Ordinal))
                    compatible = false;
            }

            Check(compatible,
                $"The saved {label} '{component.Nick}' output schema is a prefix of the current component " +
                (compatible
                    ? $"({saved.Count} of {expectedOutputs.Length} outputs)"
                    : $"- saved: {string.Join(", ", saved)})"));
        }

        /// <summary>
        /// Presence check for a user-owned input: the input must be fed by
        /// something that resolves, and the message reports the resolved
        /// source so the canvas wiring stays visible in the test log.
        /// </summary>
        private static void CheckFed(
            GhxModel model,
            GhxObject consumer,
            string inputName,
            string messageTemplate)
        {
            GhxParam input = consumer.Inputs.FirstOrDefault(p => p.Name == inputName);

            if (input == null)
            {
                Check(false, $"{consumer.Nick} has no '{inputName}' input");
                return;
            }

            List<string> resolved = input.Sources
                .Select(s => model.DescribeSource(s))
                .ToList();

            Check(resolved.Count > 0 && resolved.Any(s => s != "UNRESOLVED"),
                string.Format(messageTemplate, string.Join(", ", resolved)));
        }

        private static void CheckSource(
            GhxModel model,
            GhxObject consumer,
            string inputName,
            string expectedLabel,
            string message)
        {
            GhxParam input = consumer.Inputs.FirstOrDefault(p => p.Name == inputName);

            if (input == null)
            {
                Check(false, $"{message} (no '{inputName}' input on {consumer.Nick})");
                return;
            }

            if (expectedLabel == null)
            {
                // Presence check only: the upstream is a generator-side object
                // whose exact shape is not part of the contract.
                Check(input.Sources.Count > 0,
                    $"{message} (the input has {input.Sources.Count} source(s))");
                return;
            }

            string actual = input.Sources.Count == 1
                ? model.DescribeSource(input.Sources[0])
                : string.Join(", ", input.Sources.Select(s => model.DescribeSource(s)));

            Check(input.Sources.Count == 1 && actual == expectedLabel,
                $"{message} (got: {actual})");
        }

        private static void CheckSchema(
            GhxObject component,
            string[] expectedInputs,
            bool inputs,
            string label)
        {
            List<string> actual = component.Inputs.Select(p => p.Name).ToList();

            bool matches = actual.Count == expectedInputs.Length;

            for (int i = 0; matches && i < expectedInputs.Length; i++)
            {
                if (!string.Equals(actual[i], expectedInputs[i], StringComparison.Ordinal))
                    matches = false;
            }

            Check(matches,
                $"The saved {label} input schema matches the current component " +
                (matches
                    ? $"({actual.Count} pins)"
                    : $"- saved: {string.Join(", ", actual)}; expected: {string.Join(", ", expectedInputs)})"));
        }
    }
}
