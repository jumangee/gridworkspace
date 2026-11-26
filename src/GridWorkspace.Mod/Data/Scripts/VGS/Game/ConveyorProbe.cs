using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Reads one grid's conveyor lines and reduces each to the two blocks its ends stand on
    /// (ADR-0040).
    ///
    /// <b>Why the object builder.</b> The game's conveyor graph is implemented in
    /// <c>Sandbox.Game.GameSystems.Conveyors</c>, and that namespace is not on the script
    /// whitelist: a mod that references <c>MyGridConveyorSystem</c> or
    /// <c>IMyConveyorEndpoint</c> compiles locally and is rejected when the game loads it. The
    /// whitelisted view of the same data is the grid's serialised object builder:
    /// <c>IMyEntity.GetObjectBuilder()</c> returns <c>MyObjectBuilder_CubeGrid</c>, whose
    /// <c>ConveyorLines</c> list is filled by <c>MyCubeGridSystems.GetObjectBuilder</c> through
    /// <c>MyGridConveyorSystem.SerializeLines</c> (verified by IL, see
    /// <c>docs/01-research/api-conveyor-network.md</c>).
    ///
    /// <b>What a line's endpoints mean.</b> Each end is a <c>ConveyorLinePosition</c>: the
    /// block's grid cell and the direction of the port. The cell is the block's own cell, not
    /// the cell in front of it — <c>MyConveyorLine.GetBlockLinePositions</c> computes it from
    /// the block's model dummies and subtracts the definition's centre. <c>GetCubeBlock(cell)</c>
    /// therefore resolves the endpoint block, including a multi-block, whose port cell is one
    /// of its cells.
    ///
    /// <b>Tubes are not vertices.</b> A line runs from endpoint block to endpoint block and may
    /// pass through any number of conveyor tubes and junctions; the game has already collapsed
    /// them into one line. That is why the raw graph is small and why the editor never has to
    /// model a tube.
    ///
    /// <b>Cost, and why it is the caller's decision.</b> <c>GetObjectBuilder</c> serialises
    /// every block of the grid, not just the lines. It is far too expensive to call per frame
    /// and is therefore read on the structural pass only, and only for grids that have at least
    /// two drawn nodes (a grid with fewer cannot produce a link between two cards). The caller
    /// decides when to pay for it; this type only performs the read.
    /// </summary>
    public static class ConveyorProbe
    {
        /// <summary>
        /// Appends one segment per conveyor line of <paramref name="grid"/> to
        /// <paramref name="into"/>, and reports whether the grid could be read at all.
        ///
        /// <b>Why the answer is a bool and not a count.</b> "This grid has no conveyor lines"
        /// and "this grid refused to be serialised" are different facts with the same count, and
        /// the caller must not confuse them: the first means the network is empty and the old
        /// picture is stale, the second means it is unknown and the old picture must stay. So the
        /// return value is <b>whether the read happened</b>, and the segments land in the list.
        ///
        /// Lines whose ends cannot be resolved to a block are skipped silently: a line with a
        /// null endpoint is a legal state in the game, not an error, and it carries no
        /// information the schema can draw.
        /// </summary>
        public static bool TryReadGrid(IMyCubeGrid grid, List<ConveyorSegment> into)
        {
            if (grid == null || into == null) return false;

            try
            {
                var builder = grid.GetObjectBuilder() as MyObjectBuilder_CubeGrid;
                if (builder == null) return false;

                List<MyObjectBuilder_ConveyorLine> lines = builder.ConveyorLines;
                if (lines == null || lines.Count == 0) return true;

                for (int i = 0; i < lines.Count; i++)
                {
                    MyObjectBuilder_ConveyorLine line = lines[i];
                    if (line == null) continue;

                    long a = ResolveEndpoint(grid, line.StartPosition);
                    if (a <= 0) continue;

                    long b = ResolveEndpoint(grid, line.EndPosition);
                    if (b <= 0) continue;

                    // A line whose ends stand on one block is a loop through tubes. It carries
                    // no information for the schema (the block would be linked to itself), and
                    // ConveyorNetwork drops it as well; dropping it here keeps the raw list
                    // honest.
                    if (a == b) continue;

                    into.Add(new ConveyorSegment(a, b));
                }
                return true;
            }
            catch (Exception e)
            {
                // The grid can be closing while the editor refreshes. "Could not read" is not
                // "there are no lines": the caller keeps the previous network, exactly as the
                // state pass keeps the previous integrity reading.
                VgsLog.Throttled("conveyor-probe", "could not read a grid's conveyor lines", e, 300);
                return false;
            }
        }

        /// <summary>
        /// Block standing on the line endpoint's cell, or 0 when the cell is empty or the
        /// endpoint cannot be read. The direction is deliberately not used: the cell alone
        /// identifies the block, and the direction would only matter if the editor wanted to
        /// reconstruct the physical route, which it does not.
        /// </summary>
        private static long ResolveEndpoint(IMyCubeGrid grid, SerializableVector3I cell)
        {
            try
            {
                // Field access rather than the implicit conversion: the conversion is not part
                // of the documented public surface of SerializableVector3I, and a compile-time
                // dependency on it would be a needless one.
                var position = new Vector3I(cell.X, cell.Y, cell.Z);

                IMySlimBlock slim = grid.GetCubeBlock(position);
                if (slim == null) return 0;

                IMyCubeBlock fat = slim.FatBlock;
                return fat != null ? fat.EntityId : 0;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("conveyor-endpoint", "could not resolve a conveyor endpoint", e, 300);
                return 0;
            }
        }
    }
}
