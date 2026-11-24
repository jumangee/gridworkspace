using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// One block an event controller watches: a member of the controller's "selected blocks"
    /// list (ADR-0017).
    ///
    /// A watch target is <b>not</b> an <see cref="Edge"/>. An edge is a connection that lives in
    /// the source block's toolbar and occupies a cell there (ADR-0011); watching has no cell and
    /// no action — the controller does not *call* the block, it *observes* it. Putting the two
    /// in one type would mean an edge without a cell, which is exactly the invariant the graph
    /// is built on.
    ///
    /// <see cref="DisplayName"/> is the watched block's name as of the last refresh. It is
    /// copied rather than resolved so the port can be labelled without the draw path ever
    /// touching a live block — and so the bar keeps its name even when the target is hidden
    /// from the schema with "Show in VGS".
    /// </summary>
    public sealed class WatchTarget
    {
        /// <summary>Entity id of the watched block.</summary>
        public readonly long EntityId;

        /// <summary>Player-visible name of the watched block. Refreshed with the graph.</summary>
        public string DisplayName; // review:allow R009 written only by the refresh pass

        /// <summary>
        /// Drawing slot: which stacked watch bar under the card this target occupies. Assigned
        /// by <see cref="GridGraph.LayoutPorts"/> — the UI never computes it.
        /// </summary>
        public int Slot; // review:allow R009 assigned only by GridGraph.LayoutPorts

        public WatchTarget(long entityId, string displayName)
        {
            EntityId = entityId;
            DisplayName = displayName;
            Slot = -1;
        }

        public override string ToString()
        {
            return EntityId + " " + DisplayName;
        }
    }

}
