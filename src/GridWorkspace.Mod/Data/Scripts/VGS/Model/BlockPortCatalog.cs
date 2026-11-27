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
    ///
    /// <b>The table is indexed by category, not by a family string</b> (ADR-0030): a subtype is
    /// classified once, by <see cref="ResolveCategory"/>, and the category's
    /// <see cref="CategoryDescriptor"/> answers what fires its toolbar. The same descriptor declares
    /// the category's own state readings and the shape that draws them (ADR-0042), so per-category
    /// knowledge stays in one row instead of spreading into a second table.
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
        /// What the editor knows about each category: the events that fire its toolbar, the keys of
        /// its own state readings, and the shape that draws them (ADR-0030, ADR-0042).
        ///
        /// A row is the whole knowledge of one category — there is no second table. Categories
        /// without a row fall back to <see cref="UnknownDescriptor"/>, which is the same thing as
        /// "no curated events": the block gets the generic event and no metrics.
        ///
        /// Only toolbar-triggering events belong here. A battery announcing a charge change to C#
        /// code cannot put anything on its own toolbar, so it is not a source in this editor.
        ///
        /// <b>The five rows that carry a widget are the five that carry metric keys</b>, and the
        /// counts have to agree — <c>StateWidget.MetricCount(widget) == metricKeys.Length</c> — which
        /// the offline gate asserts. A row with a widget and no keys would draw a bar from a number
        /// nobody writes; a row with keys and no widget would read the world for a picture that has
        /// nowhere to go.
        /// </summary>
        private static readonly Dictionary<BlockCategory, CategoryDescriptor> Descriptors =
            new Dictionary<BlockCategory, CategoryDescriptor>
            {
                { BlockCategory.Cockpit, new CategoryDescriptor(new[] { "TargetLocked", "TargetLost" }, null) },

                // A timer's widget is a countdown, not a fraction of anything: the reading is how
                // much of the delay has elapsed, and the hand returning to twelve is the trigger.
                { BlockCategory.Timer, new CategoryDescriptor(new[] { "Triggered" },
                                                              new[] { "Elapsed" }, StateWidgetKind.Clock) },

                { BlockCategory.Sensor, new CategoryDescriptor(new[] { "Detected" }, null) },
                { BlockCategory.EventController, new CategoryDescriptor(new[] { "EventTriggered" }, null) },
                { BlockCategory.ButtonPanel, new CategoryDescriptor(new[] { "ButtonPressed" }, null) },
                { BlockCategory.AirVent, new CategoryDescriptor(new[] { "Depressurized", "Pressurized" }, null) },
                { BlockCategory.Warhead, new CategoryDescriptor(new[] { "Detonated" }, null) },
                { BlockCategory.Door, new CategoryDescriptor(new[] { "OpenChanged" }, null) },
                { BlockCategory.LandingGear, new CategoryDescriptor(new[] { "LockedChanged" }, null) },
                { BlockCategory.Connector, new CategoryDescriptor(new[] { "LockedChanged" }, null) },
                { BlockCategory.SafeZone, new CategoryDescriptor(new[] { "Entered", "Left" }, null) },

                // The five state widgets (ADR-0042). A cargo container is the plainest reading —
                // how full the box is — and its shape is the one the others are variations of.
                { BlockCategory.CargoContainer, new CategoryDescriptor(null,
                                                                       new[] { "Fill" },
                                                                       StateWidgetKind.FillBar) },

                // A battery reads the same fraction and adds the one thing it knows that a
                // container does not: which way the charge is going.
                { BlockCategory.Battery, new CategoryDescriptor(null,
                                                                new[] { "Charge" },
                                                                StateWidgetKind.Battery) },

                // Two inventories, two bars, in the order the block uses them: what goes in on the
                // leading side, what comes out on the trailing one.
                { BlockCategory.Refinery, new CategoryDescriptor(null,
                                                                 new[] { "InputFill", "OutputFill" },
                                                                 StateWidgetKind.Production) },
                { BlockCategory.Assembler, new CategoryDescriptor(null,
                                                                  new[] { "InputFill", "OutputFill" },
                                                                  StateWidgetKind.Production) }
            };

        /// <summary>
        /// The descriptor of a category with no curated knowledge: no events of its own, no
        /// metrics. Shared, because "nothing known" is the same answer for every such category
        /// and a per-category instance would be a copy of the same emptiness.
        /// </summary>
        private static readonly CategoryDescriptor UnknownDescriptor =
            new CategoryDescriptor(null, null);

        /// <summary>
        /// Descriptor of a category — never null, so a caller does not have to ask whether the
        /// editor knows this block before it can ask what the block does.
        /// </summary>
        public static CategoryDescriptor Descriptor(BlockCategory category)
        {
            CategoryDescriptor descriptor;
            return Descriptors.TryGetValue(category, out descriptor) ? descriptor : UnknownDescriptor;
        }

        /// <summary>
        /// Maps a block subtype to its category. Ordered from the most specific token to the least,
        /// because several subtype names contain more than one family word and the order is the
        /// rule that decides: "LargeBlockEventController" contains no "cockpit", but
        /// "LargeBlockCockpitIndustrial" would match a sloppy "controller" test;
        /// "LargeBlockButtonPanel" and "LargeBlockSolarPanel" both contain "panel", which is the
        /// last test of all; "LargeBlockHydrogenThruster" contains both "hydrogen" and "thrust"
        /// and is a hydrogen block, not a generic thruster.
        ///
        /// <b>This is a heuristic on the subtype, and it is allowed to be wrong.</b> It decides
        /// only which curated events to offer; it never decides whether a block can drive others
        /// (that is the game's <c>IMyToolbarProvider</c>, ADR-0011) or whether it is an event
        /// controller (that is the game's <c>IMyEventControllerBlock</c>, ADR-0017). A block this
        /// method misclassifies still connects correctly — it just gets a different curated event
        /// name, or the generic one.
        ///
        /// The order below is asserted by <c>tests/OfflineChecks.cs</c> on real subtype names,
        /// including every trap listed above; do not "tidy" it.
        /// </summary>
        public static BlockCategory ResolveCategory(string subtypeId)
        {
            if (string.IsNullOrEmpty(subtypeId)) return BlockCategory.Unknown;

            string s = subtypeId.ToLowerInvariant();

            if (s.Contains("eventcontroller")) return BlockCategory.EventController;
            if (s.Contains("programmable")) return BlockCategory.Programmable;
            if (s.Contains("buttonpanel")) return BlockCategory.ButtonPanel;
            if (s.Contains("landinggear")) return BlockCategory.LandingGear;
            // Cargo containers are the one family whose real subtypes never say "cargo": the game
            // calls them LargeBlockLargeContainer, SmallBlockSmallContainer,
            // LargeBlockLargeIndustrialContainer and so on. Matching only "cargocontainer" left all
            // twenty of them Unknown (ADR-0042 §7). "container" covers the storage blocks; the
            // decorative ones (a locker room, a bookshelf, a weapon rack, a cargo terminal) do not
            // contain it either, which is why the *widget* is decided by the game's interface and
            // this table is only its fallback.
            if (s.Contains("container") || s.Contains("cargocontainer") || s.Contains("cargosmall"))
            {
                return BlockCategory.CargoContainer;
            }

            if (s.Contains("solarpanel")) return BlockCategory.SolarPanel;
            if (s.Contains("jumpdrive")) return BlockCategory.JumpDrive;
            if (s.Contains("safezone")) return BlockCategory.SafeZone;
            if (s.Contains("oxygen") || s.Contains("airvent"))
            {
                return s.Contains("airvent") ? BlockCategory.AirVent : BlockCategory.Oxygen;
            }
            if (s.Contains("hydrogen")) return BlockCategory.Hydrogen;
            if (s.Contains("cockpit") || s.Contains("seat")) return BlockCategory.Cockpit;
            if (s.Contains("timer")) return BlockCategory.Timer;
            if (s.Contains("sensor")) return BlockCategory.Sensor;
            if (s.Contains("battery")) return BlockCategory.Battery;
            if (s.Contains("thrust")) return BlockCategory.Thruster;
            if (s.Contains("gyro")) return BlockCategory.Gyro;
            if (s.Contains("connector")) return BlockCategory.Connector;
            if (s.Contains("warhead")) return BlockCategory.Warhead;
            if (s.Contains("door")) return BlockCategory.Door;
            if (s.Contains("piston")) return BlockCategory.Piston;
            if (s.Contains("rotor")) return BlockCategory.Rotor;
            if (s.Contains("hinge")) return BlockCategory.Hinge;
            if (s.Contains("reactor")) return BlockCategory.Reactor;
            if (s.Contains("turret") || s.Contains("gatling") || s.Contains("missile")) return BlockCategory.Turret;
            if (s.Contains("drill")) return BlockCategory.Drill;
            if (s.Contains("welder")) return BlockCategory.Welder;
            if (s.Contains("grinder")) return BlockCategory.Grinder;
            if (s.Contains("assembler")) return BlockCategory.Assembler;
            if (s.Contains("refinery")) return BlockCategory.Refinery;
            if (s.Contains("projector")) return BlockCategory.Projector;
            if (s.Contains("antenna")) return BlockCategory.Antenna;
            if (s.Contains("beacon")) return BlockCategory.Beacon;
            if (s.Contains("light") || s.Contains("lcd") || s.Contains("panel")) return BlockCategory.Light;

            return BlockCategory.Unknown;
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
            if (node == null) return;

            node.Events.Clear();
            node.CanDriveBlocks = false;

            if (node.Toolbar == null || !node.Toolbar.CanDrive) return;

            node.CanDriveBlocks = true;

            // The category's own events, or the generic one when it has none. A category with no
            // curated entry is not an error: a modded timer-like block still gets a usable port
            // (ADR-0002), which is what keeps the table from becoming a whitelist.
            CategoryDescriptor descriptor = Descriptor(node.Category);
            string[] specific = descriptor.Events;

            int baseCount = specific.Length > 0 ? specific.Length : 1;
            int total = node.Toolbar.YesNo ? baseCount * 2 : baseCount;
            int index = 0;

            if (specific.Length == 0)
            {
                if (node.Toolbar.YesNo)
                {
                    AddEvent(node, GenericEventKey, PortGroup.Yes, index++, total);
                    AddEvent(node, GenericEventKey, PortGroup.No, index++, total);
                }
                else
                {
                    AddEvent(node, GenericEventKey, PortGroup.Single, index++, total);
                }
                return;
            }

            for (int i = 0; i < specific.Length; i++)
            {
                string key = specific[i];
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
        ///
        /// <b>The caption is the game's own action name, not the id</b> (ADR-0037). The id is a
        /// save-data key — "OnOff", "OnOff_On", "IncreaseTextPaddingSlider" — and turning it into
        /// "On Off_ On" is not a translation, it is a misreading: the game already has a localized
        /// name for every action ("Toggle block On", "Increase Text Padding"), it is what the
        /// vanilla toolbar shows, and it is what the player recognizes. <see cref="Humanize"/> is
        /// only the fallback for an action that reports no name at all.
        ///
        /// <paramref name="actionNames"/> is parallel to <paramref name="actionIds"/> and may be
        /// null or shorter, in which case the remaining captions fall back to the id.
        /// </summary>
        public static void FillActions(Node node, IList<string> actionIds, IList<string> actionNames)
        {
            node.Actions.Clear();
            if (actionIds == null) return;

            int count = actionIds.Count;
            for (int i = 0; i < count; i++)
            {
                string id = actionIds[i];
                if (string.IsNullOrEmpty(id)) continue;

                string name = actionNames != null && i < actionNames.Count ? actionNames[i] : null;
                if (string.IsNullOrEmpty(name)) name = Humanize(id);

                float anchor = count == 1 ? 0.5f : (float)i / (count - 1);
                node.Actions.Add(new Port(id, name, PortKind.Action, PortDirection.Input, anchor));
            }
        }

        /// <summary>
        /// Builds action ports without display names, so every caption is derived from the id.
        /// Kept for callers that have no names to give — the offline checks, which assert the
        /// placement rules and not the captions.
        /// </summary>
        public static void FillActions(Node node, IList<string> actionIds)
        {
            FillActions(node, actionIds, null);
        }

        /// <summary>
        /// The actions a block group accepts, computed the way the game computes them for a
        /// group toolbar item (ADR-0015, verified in
        /// <c>docs/01-research/api-block-groups.md</c> §3).
        ///
        /// The rule, in the game's own order:
        ///
        ///  1. all members share one runtime type → that type's actions, keeping those enabled
        ///     on <b>at least one</b> member;
        ///  2. different types, all members functional → the actions enabled on <b>every</b>
        ///     member, in the first member's order;
        ///  3. different types and at least one member is not a functional block → no actions
        ///     at all.
        ///
        /// Point 3 is not a simplification: the game's own <c>GetActions</c> returns an empty
        /// list in exactly that case, so a group with one conveyor tube in it really does
        /// offer nothing in the vanilla toolbar.
        ///
        /// Point 2 is where a plausible guess is wrong. There is no "most common ancestor"
        /// search: the game intersects action *ids*. A shared base class matters only when the
        /// item fires, where an action is applied to a block whose type derives from the type
        /// the action was registered for.
        ///
        /// The asymmetry between "any" in branch 1 and "every" in branch 2 is also the game's:
        /// with one type it asks each action whether <em>some</em> member has it enabled, and
        /// with several it builds one list per member and intersects them. Treating branch 1 as
        /// "every" would hide actions from a group that contains a switched-off block.
        ///
        /// The result is a list of ids rather than ports so that the rule is a pure function
        /// of the members and can be asserted in <c>tests/OfflineChecks.cs</c>. The caller
        /// turns it into ports with <see cref="FillActions"/> only when it actually changed,
        /// which keeps existing <see cref="Port"/> objects — and therefore the user's
        /// selection and open menu — alive across refreshes.
        /// </summary>
        public static void ComputeGroupActionIds(IList<GroupMemberActions> members, List<string> into)
        {
            ComputeGroupActionIds(members, into, null);
        }

        /// <summary>
        /// Same rule, and additionally the game's display name of every surviving action.
        ///
        /// The names are taken from the member whose list the id came from — always the first
        /// readable member, because both branches below walk its lists — so a group's ports are
        /// captioned exactly like the same block's own ports, not with a second naming scheme.
        /// <paramref name="intoNames"/> may be null when the caller does not need captions.
        /// </summary>
        public static void ComputeGroupActionIds(IList<GroupMemberActions> members, List<string> into,
                                                 List<string> intoNames)
        {
            if (into == null) return;
            into.Clear();
            if (intoNames != null) intoNames.Clear();
            if (members == null || members.Count == 0) return;

            // The first readable member defines the base: its type and its action order. A
            // null entry is a member nobody could read, and skipping it is safer than letting
            // one unreadable member empty the group.
            GroupMemberActions first = null;
            for (int i = 0; i < members.Count && first == null; i++) first = members[i];
            if (first == null) return;

            string firstType = first.TypeKey;
            bool sameType = true;
            bool allFunctional = first.IsFunctional;

            for (int i = 0; i < members.Count; i++)
            {
                GroupMemberActions member = members[i];
                if (member == null || ReferenceEquals(member, first)) continue;

                if (!string.Equals(member.TypeKey, firstType, StringComparison.Ordinal)) sameType = false;
                if (!member.IsFunctional) allFunctional = false;
            }

            if (sameType)
            {
                // "Enabled on any member", in the type's own order.
                for (int i = 0; i < first.ActionIds.Count; i++)
                {
                    string id = first.ActionIds[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (ContainsId(into, id)) continue;
                    if (EnabledAnywhere(members, id)) Add(into, intoNames, id, NameOf(first, i));
                }
                return;
            }

            // Mixed types with a non-functional member: the game offers nothing, and a port
            // that cannot be executed would be a promise the mod cannot keep.
            if (!allFunctional) return;

            IList<string> enabledFirst = first.EnabledActionIds;
            for (int i = 0; i < enabledFirst.Count; i++)
            {
                string id = enabledFirst[i];
                if (string.IsNullOrEmpty(id)) continue;
                if (ContainsId(into, id)) continue;

                bool everywhere = true;
                for (int m = 0; m < members.Count && everywhere; m++)
                {
                    GroupMemberActions member = members[m];
                    if (member == null || ReferenceEquals(member, first)) continue;
                    if (!ContainsId(member.EnabledActionIds, id)) everywhere = false;
                }
                if (everywhere) Add(into, intoNames, id, NameOf(first, id));
            }
        }

        private static void Add(List<string> ids, List<string> names, string id, string name)
        {
            ids.Add(id);
            if (names != null) names.Add(name);
        }

        /// <summary>
        /// Display name of the action at position <paramref name="index"/> of a member's own
        /// list, or null when the member carries no names.
        /// </summary>
        private static string NameOf(GroupMemberActions member, int index)
        {
            IList<string> names = member.ActionNames;
            if (names == null || index < 0 || index >= names.Count) return null;
            return names[index];
        }

        /// <summary>Display name of an action id within one member's list, or null.</summary>
        private static string NameOf(GroupMemberActions member, string id)
        {
            IList<string> names = member.ActionNames;
            if (names == null) return null;

            IList<string> ids = member.ActionIds;
            for (int i = 0; i < ids.Count && i < names.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal)) return names[i];
            }
            return null;
        }

        private static bool EnabledAnywhere(IList<GroupMemberActions> members, string id)
        {
            for (int i = 0; i < members.Count; i++)
            {
                GroupMemberActions member = members[i];
                if (member != null && ContainsId(member.EnabledActionIds, id)) return true;
            }
            return false;
        }

        private static bool ContainsId(IList<string> ids, string id)
        {
            if (ids == null) return false;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
            }
            return false;
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

        /// <summary>
        /// True when an action id is one the game builds from the given control id.
        ///
        /// The game derives every action id from its control's id, in exactly three shapes
        /// [ПРОВЕРЕНО по IL: `MyTerminalControlExtensions.EnableAction` — the control's own id,
        /// `EnableOnOffActions` — `control.Id + "_On"` / `"_Off"`, `EnableActions` —
        /// `"Increase"` / `"Decrease"` + `control.Id`]. So "which control does this action belong
        /// to" needs no table of action names, and a control the game hides for a block hides its
        /// actions with it (ADR-0039).
        /// </summary>
        public static bool ActionBelongsToControl(string actionId, string controlId)
        {
            if (string.IsNullOrEmpty(actionId) || string.IsNullOrEmpty(controlId)) return false;

            if (string.Equals(actionId, controlId, StringComparison.Ordinal)) return true;

            return HasSuffix(actionId, controlId, "_On")
                || HasSuffix(actionId, controlId, "_Off")
                || HasPrefix(actionId, "Increase", controlId)
                || HasPrefix(actionId, "Decrease", controlId);
        }

        /// <summary>
        /// True when the action belongs to any of the controls the game hides for this block.
        /// Allocation-free and ordinal: it runs once per action of a block type.
        /// </summary>
        public static bool IsActionHiddenBy(string actionId, IList<string> hiddenControlIds)
        {
            if (hiddenControlIds == null) return false;

            for (int i = 0; i < hiddenControlIds.Count; i++)
            {
                if (ActionBelongsToControl(actionId, hiddenControlIds[i])) return true;
            }
            return false;
        }

        private static bool HasSuffix(string id, string prefix, string suffix)
        {
            if (id.Length != prefix.Length + suffix.Length) return false;
            return string.CompareOrdinal(id, 0, prefix, 0, prefix.Length) == 0
                && string.CompareOrdinal(id, prefix.Length, suffix, 0, suffix.Length) == 0;
        }

        private static bool HasPrefix(string id, string prefix, string rest)
        {
            if (id.Length != prefix.Length + rest.Length) return false;
            return string.CompareOrdinal(id, 0, prefix, 0, prefix.Length) == 0
                && string.CompareOrdinal(id, prefix.Length, rest, 0, rest.Length) == 0;
        }
    }
}
