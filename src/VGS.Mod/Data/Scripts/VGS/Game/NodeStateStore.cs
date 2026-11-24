using System;
using System.Collections.Generic;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Reads and writes the per-block state the schema needs beyond the toolbar: where the
    /// card sits on the canvas and whether the block is shown at all (ADR-0012, ADR-0013).
    ///
    /// <b>Why the state lives on the block.</b> The obvious design — one table on the grid,
    /// keyed by block entity id — breaks the moment the ship is copied, projected or split:
    /// <c>MyObjectBuilder_CubeBlock.Remap</c> reassigns every block id
    /// (<c>RemapEntityId</c>) and does not touch our strings, so the table would silently point
    /// at ids that no longer exist. Stored on the block itself, the data rides inside the
    /// block's own <c>ComponentContainer</c> and travels with it through save/load, blueprints,
    /// projection and grid splitting, with no key at all.
    ///
    /// <b>Why the component is not created on read.</b> <see cref="IsShown"/> and
    /// <see cref="TryReadPosition"/> are called for every terminal block on every graph
    /// refresh. Creating a storage component for each of them would attach a component to
    /// every block in the world — a change to the save file caused by merely opening the
    /// editor. Reads therefore never create; only a real write does.
    ///
    /// <b>Registration is part of the contract.</b> Both keys must appear in
    /// <c>Data/EntityComponents.sbc</c>. <c>MyModStorageComponent.Serialize</c> copies only
    /// registered GUIDs into the save and logs
    /// <c>"Not saving ModStorageComponent GUID: ... not claimed"</c> for the rest — the data
    /// would survive in memory and vanish on reload, which is the worst possible failure mode
    /// because it looks like it works until the player reloads.
    ///
    /// Everything here is guarded: the methods are called from terminal-control callbacks (the
    /// game's UI thread) and from the graph refresh, and neither may see an exception.
    /// </summary>
    public static class NodeStateStore
    {
        /// <summary>Stored value of "shown". Absent means shown, so this is written explicitly.</summary>
        private const string ShownValue = "1";

        /// <summary>Stored value of "hidden". Any other value, including none, means shown.</summary>
        private const string HiddenValue = "0";

        /// <summary>
        /// True unless the player turned the block off in the terminal.
        ///
        /// Defaulting to true is what keeps the feature safe to ship: every block in every
        /// existing world has no stored value, and "no value" must mean "visible", otherwise
        /// installing the mod would empty everyone's schema.
        /// </summary>
        public static bool IsShown(IMyTerminalBlock block)
        {
            string raw;
            if (!TryRead(block, VgsConstants.VisibilityStorageKey, out raw)) return true;

            return !string.Equals(raw, HiddenValue, StringComparison.Ordinal);
        }

        /// <summary>
        /// Stores the visibility flag. The value is written as <c>"0"</c> or <c>"1"</c> rather
        /// than removed/added, so that a flag turned off and on again leaves no ambiguity about
        /// what "absent" means.
        /// </summary>
        public static void SetShown(IMyTerminalBlock block, bool shown)
        {
            Write(block, VgsConstants.VisibilityStorageKey, shown ? ShownValue : HiddenValue);
        }

        /// <summary>
        /// Reads the saved canvas position. False means "no usable position was saved" — the
        /// node then gets the automatic layout, which is the correct answer both for a block
        /// that was never moved and for a value that failed to parse.
        /// </summary>
        public static bool TryReadPosition(IMyTerminalBlock block, out Vector2 position)
        {
            position = Vector2.Zero;

            string raw;
            if (!TryRead(block, VgsConstants.LayoutStorageKey, out raw)) return false;

            return NodeStateFormat.TryParsePosition(raw, out position);
        }

        /// <summary>
        /// Applies the stored state to a freshly created node. Called once, from
        /// <c>GridGraphBuilder.CreateNode</c>, where the live block is already in hand.
        ///
        /// A saved position also marks the node placed (that is what <see cref="Node.Position"/>
        /// does), which is what stops <see cref="GridGraph.LayoutUnplaced"/> from moving a card
        /// the player positioned in an earlier session.
        /// </summary>
        public static void ApplyTo(Node node, IMyTerminalBlock block)
        {
            if (node == null || block == null) return;

            Vector2 saved;
            if (TryReadPosition(block, out saved)) node.Position = saved;
        }

        /// <summary>
        /// Writes the node's current position to its block.
        ///
        /// Called when the player drops a card, not on every pointer move and not when the
        /// screen closes: per-move would write sixty times a second, and closing can be a
        /// session unload that never reaches <c>OnClosed</c>.
        ///
        /// Returns false when the block is gone (destroyed while the card was being dragged),
        /// which is a normal outcome rather than an error.
        /// </summary>
        public static bool SavePosition(Node node)
        {
            if (node == null) return false;

            IMyTerminalBlock block;
            if (!EntityResolver.TryResolveTerminalBlock(node.EntityId, out block))
            {
                VgsLog.Throttled("nodestate.gone", "block gone, position not saved", 300);
                return false;
            }

            try
            {
                return Write(block, VgsConstants.LayoutStorageKey,
                             NodeStateFormat.FormatPosition(node.Position));
            }
            catch (Exception e)
            {
                VgsLog.Throttled("nodestate.position", "could not save node position", e, 300);
                return false;
            }
        }

        /// <summary>
        /// Reads the watch list of an event controller: the entity ids of the blocks it
        /// observes (ADR-0017). False means "nothing is stored" — a controller that never had a
        /// block attached — and the list is left empty in that case.
        ///
        /// Like every other read here it never creates the storage component: opening the
        /// editor must not attach storage to every block in the world.
        /// </summary>
        public static bool TryReadWatchList(IMyTerminalBlock block, List<long> into)
        {
            if (into == null) return false;
            into.Clear();

            string raw;
            if (!TryRead(block, VgsConstants.WatchStorageKey, out raw)) return false;

            NodeStateFormat.ParseIds(raw, into);
            return true;
        }

        /// <summary>
        /// Stores the watch list of an event controller. The value is the id list as
        /// <c>"id id id"</c>, or an empty string for "watches nothing" — written explicitly
        /// rather than removed, so that an emptied list cannot be mistaken for "never set".
        ///
        /// Returns false when the block is gone or the write was refused; the caller then has
        /// to undo whatever it did in the graph (ADR-0017 §6).
        /// </summary>
        public static bool SaveWatchList(IMyTerminalBlock block, List<long> ids)
        {
            if (block == null) return false;

            try
            {
                return Write(block, VgsConstants.WatchStorageKey, NodeStateFormat.FormatIds(ids));
            }
            catch (Exception e)
            {
                VgsLog.Throttled("nodestate.watch", "could not save watch list", e, 300);
                return false;
            }
        }

        /// <summary>
        /// True when the block already carries a mod-storage component.
        ///
        /// The distinction matters to a caller that has to tell "this block never stored
        /// anything" from "the storage has not been attached yet": a missing component is the
        /// second case during a world load, and an attached component without our key is the
        /// first. Reading a value cannot tell them apart, because both come back as "nothing".
        /// </summary>
        public static bool HasStorage(IMyTerminalBlock block)
        {
            if (block == null) return false;

            try
            {
                IMyEntity entity = block;
                return entity.Storage != null;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("nodestate.storage", "could not read block storage", e, 300);
                return false;
            }
        }

        /// <summary>
        /// Reads one stored value without creating the storage component. False means "nothing
        /// is stored" — the caller decides what the default is.
        /// </summary>
        private static bool TryRead(IMyTerminalBlock block, Guid key, out string value)
        {
            value = null;
            if (block == null) return false;

            try
            {
                // IMyTerminalBlock is an IMyEntity, and Storage is null until something writes
                // to it — including the game's own CustomData, which lives in this same table
                // under its own GUID. A null here is the common case, not an error.
                MyModStorageComponentBase storage = ((IMyEntity)block).Storage;
                if (storage == null) return false;

                return storage.TryGetValue(key, out value);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("nodestate.read", "could not read block state", e, 300);
                return false;
            }
        }

        /// <summary>
        /// Writes one stored value, creating the storage component on first write.
        ///
        /// The component is attached by assigning the entity's <c>Storage</c> property, which is
        /// what the game itself does for <c>CustomData</c>: the setter calls
        /// <c>Components.Add(value)</c>, and the entity's component-added hook is what makes
        /// <c>get_Storage()</c> return it afterwards. Adding it to the container by hand would
        /// leave that field unset.
        /// </summary>
        private static bool Write(IMyTerminalBlock block, Guid key, string value)
        {
            if (block == null || value == null) return false;

            try
            {
                IMyEntity entity = block;
                MyModStorageComponentBase storage = entity.Storage;
                if (storage == null)
                {
                    storage = new MyModStorageComponent();
                    entity.Storage = storage;
                }

                storage.SetValue(key, value);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("nodestate.write", "could not write block state", e, 300);
                return false;
            }
        }
    }
}
