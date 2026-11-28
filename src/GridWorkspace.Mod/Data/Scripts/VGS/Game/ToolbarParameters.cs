using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.ModAPI;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// The one place where the game's action-argument vocabulary meets the model's
    /// (<see cref="ActionParameter"/>, <see cref="ParameterSet"/>) — ADR-0050.
    ///
    /// <b>What the game publishes, and what it does not.</b> Verified against the installed
    /// assemblies (<c>docs/01-research/api-action-parameters.md</c>):
    ///
    ///  * an action declares the <em>types</em> of its arguments through
    ///    <c>IMyTerminalAction.ParameterDefinitions</c>, reachable from a block with
    ///    <c>MyToolbarApiExtensions.GetActionParameterDefinitions(block, actionId, list)</c> —
    ///    a <c>List&lt;TypeCode&gt;</c> and nothing else: no names, no bounds, no defaults;
    ///  * a toolbar item carries the values inside itself
    ///    (<c>MyObjectBuilder_ToolbarItemTerminal.Parameters</c>), and they are readable with
    ///    <c>MyToolbarApiExtensions.GetParameters(slots, index, list)</c>;
    ///  * they are written by handing the values to the same extension
    ///    (<c>Set(builder, index, item, parameters)</c>), which serialises each one through
    ///    <c>TerminalActionParameter.GetObjectBuilder</c>.
    ///
    /// So "does this action need values" and "which values does this connection pass" are both
    /// answerable exactly, and neither needs a table of subtypes. What is <b>not</b> answerable is
    /// the range a value must be in: the vanilla dialog's min/max live in the block's own code,
    /// not in the metadata. That is why <see cref="ActionParameter.TryCreate"/> checks the type's
    /// range and not the action's.
    ///
    /// <b>Every entry point is a boundary.</b> This runs from the refresh pass and from a click
    /// handler, on blocks that may be destroyed mid-frame, so nothing here throws.
    /// </summary>
    public static class ToolbarParameters
    {
        /// <summary>
        /// The argument types the block's action declares, in the order the game asks for them,
        /// or null when the action takes none. Null rather than an empty array on purpose: the
        /// caller stores it on the port, "no arguments" is then one comparison, and the offline
        /// gate can tell a port whose types were read from one whose were not.
        /// </summary>
        public static TypeCode[] Definitions(IMyTerminalBlock block, string actionId)
        {
            if (block == null || string.IsNullOrEmpty(actionId)) return null;

            try
            {
                var codes = new List<TypeCode>(4);
                MyToolbarApiExtensions.GetActionParameterDefinitions(block, actionId, codes);
                if (codes.Count == 0) return null;

                var types = new TypeCode[codes.Count];
                for (int i = 0; i < types.Length; i++) types[i] = codes[i];
                return types;
            }
            catch (Exception e)
            {
                // A block that cannot answer keeps its action, with no arguments: refusing the
                // action would hide something the player can see in the terminal, and the worst
                // case is an item written without values the game then ignores.
                VgsLog.Throttled("action-parameters", "could not read an action's parameter types", e, 600);
                return null;
            }
        }

        /// <summary>
        /// The arguments the item in this toolbar cell carries, or
        /// <see cref="ParameterSet.None"/> when it carries none — which is also what an
        /// unreadable cell answers.
        ///
        /// <b>This is how a connection edited in the vanilla terminal reaches the schema</b>
        /// (ADR-0018): the reconcile pass reads the cell, and the arguments come with it, so the
        /// input bar shows the new values without the editor having to be reopened.
        /// </summary>
        public static ParameterSet Read(IMyToolbar toolbar, int index)
        {
            if (toolbar == null || index < 0 || index >= toolbar.ItemCount) return ParameterSet.None;

            try
            {
                var values = new List<Sandbox.ModAPI.Ingame.TerminalActionParameter>(4);
                MyToolbarApiExtensions.GetParameters(toolbar, index, values);
                if (values.Count == 0) return ParameterSet.None;

                var items = new ActionParameter[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    Sandbox.ModAPI.Ingame.TerminalActionParameter value = values[i];
                    items[i] = ActionParameter.FromValue(value.TypeCode, value.Value);
                }
                return ParameterSet.Of(items);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("cell-parameters", "could not read a toolbar item's parameters", e, 300);
                return ParameterSet.None;
            }
        }

        /// <summary>
        /// The values as the game's own parameter objects, for writing into a toolbar item, or
        /// null when there are none.
        ///
        /// <b>Null, not an empty list.</b> The game's builder treats null as "this item has no
        /// parameters", and null is what its own vanilla path passes; it also keeps the
        /// serialised item free of an empty <c>Parameters</c> element, so an unparameterised
        /// connection stays byte-identical to the one older versions wrote.
        ///
        /// A value the game refuses to deserialise is reported as null for the whole set: a
        /// half-filled item would be an item the action cannot run. That case cannot be reached
        /// from the dialog — it validates every value first — but it can be reached from a graph
        /// read out of a world saved by an older version.
        /// </summary>
        public static List<Sandbox.ModAPI.Ingame.TerminalActionParameter> ToGame(ParameterSet parameters)
        {
            if (parameters == null || parameters.IsEmpty) return null;

            var values = new List<Sandbox.ModAPI.Ingame.TerminalActionParameter>(parameters.Count);
            for (int i = 0; i < parameters.Count; i++)
            {
                ActionParameter p = parameters[i];
                if (p.IsEmpty) return null;

                try
                {
                    Sandbox.ModAPI.Ingame.TerminalActionParameter value =
                        Sandbox.ModAPI.Ingame.TerminalActionParameter.Deserialize(p.Text, p.Code);
                    if (value.IsEmpty) return null;
                    values.Add(value);
                }
                catch (Exception e)
                {
                    VgsLog.Throttled("parameter-deserialize", "could not build a toolbar parameter", e, 300);
                    return null;
                }
            }
            return values;
        }

        /// <summary>
        /// Writes one item into a cell, with its arguments, through the ModAPI's own extension.
        ///
        /// <b>Why not <c>change.Set(index, item)</c>.</b> That overload exists and works, but it
        /// has no parameter list: an item written with it carries none, and for a parameterised
        /// action that is a different item. The extension takes the values and is also the safe
        /// call — it rejects a builder it does not recognise instead of throwing.
        /// </summary>
        public static void Set(IMyToolbarChangeBuilder change, int index, MyToolbarItemInfo item,
                               ParameterSet parameters)
        {
            if (change == null) return;
            MyToolbarApiExtensions.Set(change, index, item, ToGame(parameters));
        }

        /// <summary>
        /// True when the cell holds the item this connection is: the same kind of target, the same
        /// action and the same arguments.
        ///
        /// One method rather than two comparisons, because the writer's read-back check and the
        /// reconcile pass must never disagree about what "the item is still there" means: a cell
        /// whose arguments were changed in the terminal is a different item, and the connection
        /// has to follow it rather than stay behind.
        /// </summary>
        public static bool SameItem(CellContents cell, Edge edge)
        {
            if (cell.IsEmpty || edge == null) return false;

            return edge.TargetsGroup
                ? cell.HoldsGroupAction(edge.TargetGroupName, edge.TargetPortKey, edge.Parameters)
                : cell.HoldsBlockAction(edge.TargetEntityId, edge.TargetPortKey, edge.Parameters);
        }
    }
}
