using System;
using System.Collections.Generic;

namespace Vgs.Model
{
    /// <summary>
    /// Describes which ports a block type exposes.
    ///
    /// Two very different sources feed this, and the asymmetry is the single most important
    /// architectural fact of the project:
    ///
    ///  * <b>Actions</b> (input ports) are discoverable at runtime. Every terminal block
    ///    reports its actions through <c>IMyTerminalBlock.GetActions()</c>, so the editor can
    ///    show the real list for any block, including blocks added by other mods, with no
    ///    hardcoded data.
    ///
    ///  * <b>Events</b> (output ports) are <b>not</b> discoverable. Space Engineers has no
    ///    API that enumerates what a block can raise; sensors, event controllers and
    ///    cockpits expose their behaviour as component state, not as a metadata list. The
    ///    only honest options are (a) a curated table, or (b) polling state and synthesising
    ///    events from changes. This class is the curated table; <c>Vgs.Game.EventSources</c>
    ///    is the polling half.
    ///
    /// Because the table is curated, it must degrade gracefully: an unknown block type still
    /// gets a node with its real actions and the shared events, rather than an empty node the
    /// user cannot reason about.
    /// </summary>
    public static class BlockPortCatalog
    {
        /// <summary>Generic event every block gets, backed by polling its OnOff state.</summary>
        public const string GenericOutputKey = "StateChanged";

        /// <summary>Events that every functional block shares, in display order.</summary>
        private static readonly string[] CommonOutputs =
        {
            GenericOutputKey,
            "BlockAdded",
            "BlockRemoved",
            "IntegrityChanged",
            "OnOffChanged"
        };

        /// <summary>
        /// Curated per-family events. Key is a family prefix matched against the block
        /// subtype with <see cref="ResolveFamily"/>, so "LargeBlockCockpitIndustrial" and
        /// "SmallBlockCockpit" both resolve to "cockpit".
        /// </summary>
        private static readonly Dictionary<string, string[]> FamilyOutputs =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "cockpit", new[] { "OccupiedChanged", "HandBrakeChanged", "DampenersChanged", "ControlAcquired" } },
                { "timer", new[] { "Triggered" } },
                { "sensor", new[] { "DetectedChanged", "EntityDetected" } },
                { "eventcontroller", new[] { "EventTriggered", "EventTriggeredElse" } },
                { "battery", new[] { "StoredPowerChanged", "ChargeModeChanged", "FullyCharged", "FullyDischarged" } },
                { "thruster", new[] { "ThrustChanged" } },
                { "gyro", new[] { "OverriddenChanged" } },
                { "connector", new[] { "LockedChanged", "ConnectedChanged" } },
                { "landinggear", new[] { "LockedChanged" } },
                { "warhead", new[] { "CountdownChanged", "Detonated" } },
                { "door", new[] { "OpenChanged" } },
                { "piston", new[] { "PositionChanged", "AtLimit" } },
                { "rotor", new[] { "AngleChanged", "AtLimit" } },
                { "hinge", new[] { "AngleChanged", "AtLimit" } },
                { "cargocontainer", new[] { "FillRatioChanged", "Full", "Empty" } },
                { "reactor", new[] { "FuelChanged", "OutOfFuel" } },
                { "solarpanel", new[] { "OutputChanged" } },
                { "oxygen", new[] { "FillRatioChanged" } },
                { "hydrogen", new[] { "FillRatioChanged" } },
                { "airvent", new[] { "Depressurized", "Pressurized" } },
                { "light", new[] { "IntensityChanged" } },
                { "antenna", new[] { "BroadcastChanged" } },
                { "beacon", new[] { "BroadcastChanged" } },
                { "programmable", new[] { "ScriptRun", "ScriptFailed" } },
                { "turret", new[] { "TargetAcquired", "TargetLost", "FiringChanged" } },
                { "drill", new[] { "DrillingChanged" } },
                { "welder", new[] { "WeldingChanged" } },
                { "grinder", new[] { "GrindingChanged" } },
                { "assembler", new[] { "ProductionCompleted", "QueueEmpty" } },
                { "refinery", new[] { "ProductionCompleted" } },
                { "jumpdrive", new[] { "Charged", "Jumped" } },
                { "projector", new[] { "ProjectionChanged" } },
                { "safezone", new[] { "Entered", "Left" } },
                { "gate", new[] { "OpenChanged" } }
            };

        /// <summary>
        /// Maps a block subtype to a family key. Ordered from most specific to least, because
        /// several subtype names contain more than one family word (e.g.
        /// "LargeBlockSmallThrust" contains neither; "LargeBlockCockpitIndustrial" contains
        /// "cockpit").
        /// </summary>
        public static string ResolveFamily(string subtypeId)
        {
            if (string.IsNullOrEmpty(subtypeId)) return string.Empty;

            string s = subtypeId.ToLowerInvariant();

            // Order matters: check the longer/more specific tokens first.
            if (s.Contains("eventcontroller")) return "eventcontroller";
            if (s.Contains("programmable")) return "programmable";
            if (s.Contains("landinggear")) return "landinggear";
            if (s.Contains("cargocontainer") || s.Contains("cargosmall")) return "cargocontainer";
            if (s.Contains("solarpanel")) return "solarpanel";
            if (s.Contains("jumpdrive")) return "jumpdrive";
            if (s.Contains("safezone")) return "safezone";
            if (s.Contains("oxygen") || s.Contains("airvent")) return s.Contains("airvent") ? "airvent" : "oxygen";
            if (s.Contains("hydrogen")) return "hydrogen";
            if (s.Contains("cockpit") || s.Contains("seat")) return "cockpit";
            if (s.Contains("timer")) return "timer";
            if (s.Contains("sensor")) return "sensor";
            if (s.Contains("battery")) return "battery";
            if (s.Contains("thrust")) return "thruster";
            if (s.Contains("gyro")) return "gyro";
            if (s.Contains("connector")) return "connector";
            if (s.Contains("warhead")) return "warhead";
            if (s.Contains("door")) return "door";
            if (s.Contains("piston")) return "piston";
            if (s.Contains("rotor")) return "rotor";
            if (s.Contains("hinge")) return "hinge";
            if (s.Contains("reactor")) return "reactor";
            if (s.Contains("turret") || s.Contains("gatling") || s.Contains("missile")) return "turret";
            if (s.Contains("drill")) return "drill";
            if (s.Contains("welder")) return "welder";
            if (s.Contains("grinder")) return "grinder";
            if (s.Contains("assembler")) return "assembler";
            if (s.Contains("refinery")) return "refinery";
            if (s.Contains("projector")) return "projector";
            if (s.Contains("antenna")) return "antenna";
            if (s.Contains("beacon")) return "beacon";
            if (s.Contains("light") || s.Contains("lcd") || s.Contains("panel")) return "light";

            return string.Empty;
        }

        /// <summary>
        /// True when the key belongs to the shared event set, i.e. it is not evidence that
        /// the block has a family-specific behaviour worth a "+" button
        /// (<see cref="Node.CanSourceEvents"/>).
        /// </summary>
        public static bool IsCommonEvent(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            for (int i = 0; i < CommonOutputs.Length; i++)
            {
                if (string.Equals(CommonOutputs[i], key, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Builds the event (output) port list for a block subtype: family-specific events
        /// first, then the shared ones, de-duplicated and with anchors spread down the node
        /// edge.
        ///
        /// The anchor is only a hint for the catalog order; where a port is actually drawn is
        /// decided by <see cref="GridGraph.LayoutPorts"/>, and only for ports the user has
        /// connected.
        /// </summary>
        public static void FillEvents(Node node)
        {
            node.Events.Clear();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();
            bool hasOwn = false;

            string family = ResolveFamily(node.SubtypeId);
            string[] specific;
            if (family.Length > 0 && FamilyOutputs.TryGetValue(family, out specific))
            {
                for (int i = 0; i < specific.Length; i++)
                {
                    if (seen.Add(specific[i]))
                    {
                        ordered.Add(specific[i]);
                        hasOwn = true;
                    }
                }
            }

            for (int i = 0; i < CommonOutputs.Length; i++)
            {
                if (seen.Add(CommonOutputs[i])) ordered.Add(CommonOutputs[i]);
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                float anchor = ordered.Count == 1 ? 0.5f : (float)i / (ordered.Count - 1);
                node.Events.Add(new Port(ordered[i], Humanize(ordered[i]), PortKind.Event, PortDirection.Output, anchor));
            }

            // Cached here, next to the list it describes: the draw path asks this for every
            // visible card on every frame, and re-scanning the list there would be pure waste.
            node.CanSourceEvents = hasOwn;
        }

        /// <summary>
        /// Builds the action (input) port list from the actions the block actually reports.
        /// Anchors are spread so the ports line up with their catalog order.
        /// </summary>
        public static void FillActions(Node node, IList<string> actionIds)
        {
            node.Actions.Clear();
            if (actionIds == null) return;

            int count = actionIds.Count;
            for (int i = 0; i < count; i++)
            {
                string id = actionIds[i];
                if (string.IsNullOrEmpty(id)) continue;

                float anchor = count == 1 ? 0.5f : (float)i / (count - 1);
                node.Actions.Add(new Port(id, Humanize(id), PortKind.Action, PortDirection.Input, anchor));
            }
        }

        /// <summary>
        /// Turns "TriggerNow" into "Trigger Now" for display, leaving already-spaced text alone.
        /// </summary>
        public static string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            var sb = new System.Text.StringBuilder(key.Length + 6);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
