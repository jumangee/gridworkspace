using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// What a node stands for: one grid block, or one named block group of the grid.
    ///
    /// The two are drawn differently (a block is a card, a group is a light frame around the
    /// cards of its members) and they are not symmetric in what they can do: a group never
    /// starts a connection, because a group has no toolbar and nothing to fire it
    /// (ADR-0015). Everything else — an incoming port, an edge that ends on it, the action
    /// menu — works the same way, which is why a group is a <see cref="Node"/> at all.
    /// </summary>
    public enum NodeKind
    {
        /// <summary>A real grid block. Identified by <see cref="Node.EntityId"/>.</summary>
        Block = 0,

        /// <summary>
        /// A named block group of the grid's terminal system. Identified by
        /// <see cref="Node.GroupName"/>: a group is not an entity, so it has no entity id.
        /// </summary>
        Group = 1
    }

}
