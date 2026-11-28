using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// What one member block of a group contributes to the group's action list: its runtime
    /// type, whether it is a functional block, the action ids its type exposes, and which of
    /// them are enabled on this very block.
    ///
    /// These four fields are exactly what the game's own rule reads for a group toolbar item
    /// (ADR-0015, verified in `docs/01-research/api-block-groups.md` §3): the action set of a
    /// group is derived from the members' action ids, and it collapses to nothing when the
    /// members have different types and one of them is not a functional block.
    ///
    /// <see cref="ActionIds"/> and <see cref="EnabledActionIds"/> are two lists rather than one
    /// because the game treats them differently in the two branches: with a single member type
    /// an action survives when it is enabled on <em>any</em> member, and with mixed types only
    /// when it is enabled on <em>every</em> member. Collapsing them would lose that distinction,
    /// and the editor would offer an action the game will not fire.
    ///
    /// <see cref="TypeKey"/> is a string rather than a <see cref="System.Type"/> so that this
    /// type stays in <c>Vgs.Model</c> and the rule can be asserted without the game. The game
    /// layer passes the runtime type's name; the model only compares them for equality.
    /// </summary>
    public sealed class GroupMemberActions
    {
        /// <summary>Identity of the member's runtime block type. Compared for equality only.</summary>
        public readonly string TypeKey;

        /// <summary>
        /// True when the member is a functional block. A mixed group with one non-functional
        /// member has no actions at all, which is the game's rule, not a simplification.
        /// </summary>
        public readonly bool IsFunctional;

        /// <summary>Action ids the member's type exposes, in the game's order.</summary>
        public readonly IList<string> ActionIds;

        /// <summary>
        /// Display names parallel to <see cref="ActionIds"/>, as the game localizes them
        /// (ADR-0037). Empty when the caller has none; the catalog then captions the port from
        /// the id.
        ///
        /// Carried on the member rather than looked up later because a name belongs to an action
        /// of one block type: a group's ports take the caption of the member whose list the id
        /// came from, and by then the game object is gone.
        /// </summary>
        public readonly IList<string> ActionNames;

        /// <summary>
        /// Action ids of <see cref="ActionIds"/> that report <c>IsEnabled</c> for this very
        /// member. Empty when the block reports nothing enabled.
        /// </summary>
        public readonly IList<string> EnabledActionIds;

        /// <summary>
        /// Argument types parallel to <see cref="ActionIds"/>, or null when the caller has none.
        ///
        /// <b>Why the member carries them.</b> A group's port is captioned from the member whose
        /// list the id came from, and it must declare the same arguments as that member's own
        /// port — otherwise the group's input bar would not know that the action it stands for
        /// needs values, and the editor would write an item the game cannot run (ADR-0050). Like
        /// the names, they are taken from the first readable member: with mixed types the game
        /// applies the item to each member separately, and a member whose action takes different
        /// arguments ignores the call — which is the game's answer, not the editor's.
        /// </summary>
        public readonly IList<TypeCode[]> ActionParameterTypes;

        public GroupMemberActions(string typeKey, bool isFunctional, IList<string> actionIds)
            : this(typeKey, isFunctional, actionIds, actionIds)
        {
        }

        public GroupMemberActions(string typeKey, bool isFunctional, IList<string> actionIds,
                                  IList<string> enabledActionIds)
            : this(typeKey, isFunctional, actionIds, enabledActionIds, null)
        {
        }

        public GroupMemberActions(string typeKey, bool isFunctional, IList<string> actionIds,
                                  IList<string> enabledActionIds, IList<string> actionNames)
            : this(typeKey, isFunctional, actionIds, enabledActionIds, actionNames, null)
        {
        }

        public GroupMemberActions(string typeKey, bool isFunctional, IList<string> actionIds,
                                  IList<string> enabledActionIds, IList<string> actionNames,
                                  IList<TypeCode[]> actionParameterTypes)
        {
            TypeKey = typeKey ?? string.Empty;
            IsFunctional = isFunctional;
            ActionIds = actionIds ?? new List<string>(0);
            EnabledActionIds = enabledActionIds ?? new List<string>(0);
            ActionNames = actionNames ?? new List<string>(0);
            ActionParameterTypes = actionParameterTypes;
        }
    }

}
