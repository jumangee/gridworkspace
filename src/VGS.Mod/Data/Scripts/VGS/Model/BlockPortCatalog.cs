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
    /// Because the table is curated, it must degrade gracefully: a toolbar-driven block with
    /// no entry still gets the generic event, so a modded timer-like block is usable.
    ///
    /// <b>Events are gated by the toolbar.</b> A connection is an item in the source block's
    /// real toolbar (ADR-0011), so a block with no logic-driven toolbar has nowhere to put
    /// one and therefore exposes no event ports at all — even if it does announce state
    /// changes to C# subscribers. The curated list names <i>what fires the toolbar</i>, not
    /// everything the block can do.
    /// </summary>
    public static class BlockPortCatalog
    {
        /// <summary>Event a toolbar-driven block gets when its family has no curated entry.</summary>
        public const string GenericEventKey = "Triggered";

        /// <summary>Suffix appended to an event key for the "yes" fire group. Stable in save data.</summary>
        public const string TrueKeySuffix = "#true";

        /// <summary>Suffix appended to an event key for the "no" fire group. Stable in save data.</summary>
        public const string FalseKeySuffix = "#false";

        /// <summary>
        /// Curated per-family events — the ones that make the block's own toolbar fire.
        /// Key is a family prefix matched against the block subtype with
        /// <see cref="ResolveFamily"/>, so "LargeBlockCockpitIndustrial" and
        /// "SmallBlockCockpit" both resolve to "cockpit".
        ///
        /// Only toolbar-triggering events belong here. A battery announcing a charge change
        /// to C# code cannot put anything on its own toolbar, so it is not a source in this
        /// editor and has no entry.
        /// </summary>
        private static readonly Dictionary<string, string[]> FamilyEvents =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "cockpit", new[] { "TargetLocked", "TargetLost" } },
                { "timer", new[] { "Triggered" } },
                { "sensor", new[] { "Detected" } },
                { "eventcontroller", new[] { "EventTriggered" } },
                { "buttonpanel", new[] { "ButtonPressed" } },
                { "airvent", new[] { "Depressurized", "Pressurized" } },
                { "warhead", new[] { "Detonated" } },
                { "door", new[] { "OpenChanged" } },
                { "landinggear", new[] { "LockedChanged" } },
                { "connector", new[] { "LockedChanged" } },
                { "safezone", new[] { "Entered", "Left" } }
            };

        /// <summary>
        /// Maps a block subtype to a family key. Ordered from most specific to least, because
        /// several subtype names contain more than one family word (e.g.
        /// "LargeBlockCockpitIndustrial" contains "cockpit").
        /// </summary>
        public static string ResolveFamily(string subtypeId)
        {
            if (string.IsNullOrEmpty(subtypeId)) return string.Empty;

            string s = subtypeId.ToLowerInvariant();

            // Order matters: check the longer/more specific tokens first.
            if (s.Contains("eventcontroller")) return "eventcontroller";
            if (s.Contains("programmable")) return "programmable";
            if (s.Contains("buttonpanel")) return "buttonpanel";
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
        /// Builds the event (output) port list for a node: the events its toolbar can be
        /// fired by, one port per fire group, with anchors spread down the node edge.
        ///
        /// A yes/no toolbar produces two ports per event ("… · true" and "… · false"), because
        /// the two groups are independent lists of actions in the game and a connection has to
        /// pick one. A single-group toolbar produces one port per event.
        ///
        /// Sets <see cref="Node.CanDriveBlocks"/>, which is the flag the "+" button and the
        /// bind gesture depend on.
        /// </summary>
        public static void FillEvents(Node node)
        {
            node.Events.Clear();
            node.CanDriveBlocks = false;

            if (node == null || node.Toolbar == null || !node.Toolbar.CanDrive) return;

            node.CanDriveBlocks = true;

            var baseKeys = new List<string>(4);
            string family = ResolveFamily(node.SubtypeId);
            string[] specific;
            if (family.Length > 0 && FamilyEvents.TryGetValue(family, out specific))
            {
                for (int i = 0; i < specific.Length; i++) baseKeys.Add(specific[i]);
            }
            if (baseKeys.Count == 0) baseKeys.Add(GenericEventKey);

            int total = node.Toolbar.YesNo ? baseKeys.Count * 2 : baseKeys.Count;
            int index = 0;

            for (int i = 0; i < baseKeys.Count; i++)
            {
                string key = baseKeys[i];
                if (node.Toolbar.YesNo)
                {
                    AddEvent(node, key, PortGroup.Yes, index++, total);
                    AddEvent(node, key, PortGroup.No, index++, total);
                }
                else
                {
                    AddEvent(node, key, PortGroup.Single, index++, total);
                }
            }
        }

        private static void AddEvent(Node node, string baseKey, PortGroup group, int index, int total)
        {
            float anchor = total <= 1 ? 0.5f : (float)index / (total - 1);
            node.Events.Add(new Port(
                EventKey(baseKey, group),
                EventDisplayName(baseKey, group),
                PortKind.Event,
                PortDirection.Output,
                anchor,
                group));
        }

        /// <summary>
        /// Stable key of an event port for a fire group. The group is part of the key because
        /// two ports of one node cannot share a key, and because a saved graph must be able to
        /// say which of the two toolbars the connection belongs to.
        /// </summary>
        public static string EventKey(string baseKey, PortGroup group)
        {
            if (group == PortGroup.Yes) return baseKey + TrueKeySuffix;
            if (group == PortGroup.No) return baseKey + FalseKeySuffix;
            return baseKey;
        }

        /// <summary>Display name of an event port, marked with its fire group when it has one.</summary>
        public static string EventDisplayName(string baseKey, PortGroup group)
        {
            string name = Humanize(baseKey);
            if (group == PortGroup.Yes) return name + " · true";
            if (group == PortGroup.No) return name + " · false";
            return name;
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
