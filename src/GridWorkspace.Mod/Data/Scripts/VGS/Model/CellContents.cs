using System;

namespace Vgs.Model
{
    /// <summary>
    /// What a cell of a block's toolbar holds, reduced to what the editor can represent.
    ///
    /// The four cases are not decoration: the placement rule must tell "this cell is free"
    /// from "this cell holds somebody else's item", because the first may be written and the
    /// second must never be overwritten. Collapsing them into a bool is what made a whole
    /// release unable to place any connection at all (see
    /// <c>docs/review/regressions.md</c> R-009), so the distinction is a type, not a comment.
    ///
    /// Lives in <c>Vgs.Model</c> — with no reference to <c>MyToolbarItemKind</c> — so that the
    /// placement rule can be exercised by <c>tools/offline-check.ps1</c> against real cell
    /// contents. <c>Vgs.Game.ToolbarWriter</c> maps the game's kind onto <see cref="CellKind"/>;
    /// that mapping is the only place the two vocabularies meet.
    /// </summary>
    public enum CellKind
    {
        /// <summary>Nothing at all in the cell: it can take a connection.</summary>
        Empty = 0,

        /// <summary>A block-plus-action item: the ordinary "block → block, action" connection.</summary>
        BlockAction = 1,

        /// <summary>
        /// A group-plus-action item: one item that drives every functional member of a named
        /// block group (ADR-0015).
        /// </summary>
        GroupAction = 2,

        /// <summary>
        /// An item the editor cannot draw — a block definition, an inventory item, or a
        /// toolbar item whose identity the game did not report. It is <b>not</b> a free cell:
        /// writing over it would silently destroy wiring the player made by hand.
        /// </summary>
        Other = 3
    }

    /// <summary>
    /// The contents of one toolbar cell: a kind, plus the identity of the item when the editor
    /// knows it (a block id, or a group name, plus an action id).
    ///
    /// <b>Why this is a model type and not a game type.</b> The whole placement rule —
    /// "take the first free cell of the right slot on the first page that does not already
    /// hold this item" — is decidable from this value alone. Keeping it here means the rule
    /// and its input are compiled together by <c>tools/offline-check.ps1</c>, which is the only
    /// gate that checks logic without starting the game. When the value lived in
    /// <c>Vgs.Game</c> the planner had to receive a pre-computed <c>bool</c> instead, and an
    /// inverted argument turned into "the toolbar is full" for every empty toolbar (R-009).
    ///
    /// Immutable: it describes a reading of the toolbar at one moment, and a cell that changed
    /// is a different value. The factory methods are the only way to build one, so a
    /// <see cref="CellKind.BlockAction"/> always carries a block id and an action id.
    /// </summary>
    public struct CellContents
    {
        /// <summary>Which of the four cases this cell is.</summary>
        public readonly CellKind Kind;

        /// <summary>Entity id of the target block for <see cref="CellKind.BlockAction"/>, otherwise zero.</summary>
        public readonly long BlockEntityId;

        /// <summary>Name of the target block group for <see cref="CellKind.GroupAction"/>, otherwise null.</summary>
        public readonly string GroupName;

        /// <summary>Action id the item applies, or null when the cell holds no known action.</summary>
        public readonly string ActionId;

        /// <summary>
        /// The arguments the item passes to its action, or <see cref="ParameterSet.None"/>
        /// (ADR-0050). They are part of the item's identity for the same reason the action id is:
        /// two items that name the same block and action but pass different arguments are two
        /// different requests, and a cell holding one of them does not hold the other.
        /// </summary>
        public readonly ParameterSet Parameters;

        /// <summary>
        /// Name of the game's item kind for <see cref="CellKind.Other"/>, otherwise null. Kept
        /// only so a log line can say what was found instead of "something"; it never affects
        /// a decision.
        /// </summary>
        public readonly string Label;

        /// <summary>An empty cell: the only value the planner may write into.</summary>
        public static readonly CellContents Empty =
            new CellContents(CellKind.Empty, 0, null, null, null, ParameterSet.None);

        private CellContents(CellKind kind, long blockEntityId, string groupName, string actionId,
                             string label, ParameterSet parameters)
        {
            Kind = kind;
            BlockEntityId = blockEntityId;
            GroupName = groupName;
            ActionId = actionId;
            Label = label;
            Parameters = parameters ?? ParameterSet.None;
        }

        /// <summary>A block-plus-action item with no arguments.</summary>
        public static CellContents BlockAction(long blockEntityId, string actionId)
        {
            return BlockAction(blockEntityId, actionId, ParameterSet.None);
        }

        /// <summary>A block-plus-action item, with the arguments its toolbar item carries.</summary>
        public static CellContents BlockAction(long blockEntityId, string actionId, ParameterSet parameters)
        {
            return new CellContents(CellKind.BlockAction, blockEntityId, null, actionId, null, parameters);
        }

        /// <summary>A group-plus-action item with no arguments.</summary>
        public static CellContents GroupAction(string groupName, string actionId)
        {
            return GroupAction(groupName, actionId, ParameterSet.None);
        }

        /// <summary>A group-plus-action item, with the arguments its toolbar item carries.</summary>
        public static CellContents GroupAction(string groupName, string actionId, ParameterSet parameters)
        {
            return new CellContents(CellKind.GroupAction, 0, groupName, actionId, null, parameters);
        }

        /// <summary>
        /// An item the editor cannot draw. <paramref name="label"/> names it for the log and
        /// may be null.
        /// </summary>
        public static CellContents Other(string label)
        {
            return new CellContents(CellKind.Other, 0, null, null, label, ParameterSet.None);
        }

        /// <summary>True only for <see cref="CellKind.Empty"/> — the single writable case.</summary>
        public bool IsEmpty { get { return Kind == CellKind.Empty; } }

        /// <summary>True for a block-plus-action item.</summary>
        public bool IsBlockAction { get { return Kind == CellKind.BlockAction; } }

        /// <summary>True for a group-plus-action item.</summary>
        public bool IsGroupAction { get { return Kind == CellKind.GroupAction; } }

        /// <summary>
        /// True when the cell describes exactly this block-plus-action connection, arguments
        /// included: a cell holding <c>Run</c> with one argument does not hold <c>Run</c> with
        /// another, and treating the two as equal would leave the picture showing arguments the
        /// block does not have.
        /// </summary>
        public bool HoldsBlockAction(long blockEntityId, string actionId)
        {
            return HoldsBlockAction(blockEntityId, actionId, ParameterSet.None);
        }

        /// <summary>Same, with the arguments the connection asks for.</summary>
        public bool HoldsBlockAction(long blockEntityId, string actionId, ParameterSet parameters)
        {
            return IsBlockAction && BlockEntityId == blockEntityId
                   && string.Equals(ActionId, actionId, StringComparison.Ordinal)
                   && Parameters.SameAs(parameters);
        }

        /// <summary>True when the cell describes exactly this group-plus-action connection.</summary>
        public bool HoldsGroupAction(string groupName, string actionId)
        {
            return HoldsGroupAction(groupName, actionId, ParameterSet.None);
        }

        /// <summary>Same, with the arguments the connection asks for.</summary>
        public bool HoldsGroupAction(string groupName, string actionId, ParameterSet parameters)
        {
            return IsGroupAction && string.Equals(GroupName, groupName, StringComparison.Ordinal)
                   && string.Equals(ActionId, actionId, StringComparison.Ordinal)
                   && Parameters.SameAs(parameters);
        }

        public override string ToString()
        {
            if (IsEmpty) return "empty";
            if (IsGroupAction) return "group:" + GroupName + "." + ActionId + " " + Parameters;
            if (IsBlockAction) return BlockEntityId + "." + ActionId + " " + Parameters;
            return Label == null ? "other" : "other:" + Label;
        }
    }
}
