using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Vgs.Core;

namespace Vgs.Game
{
    /// <summary>
    /// Turns an entity id back into a live terminal block: the one implementation of a lookup
    /// that used to be copied into every caller that had an id and needed the block behind it.
    ///
    /// <b>Why a helper and not five copies.</b> An id that no longer resolves is a normal
    /// outcome, not a defect — the block may have been ground down between the refresh that drew
    /// the card and the click that uses it — and every caller has to answer it the same way,
    /// "there is no block", without throwing into the game loop. Five copies meant five answers,
    /// five throttle tags (<c>resolve-block</c>, <c>ec-resolve</c>, <c>watch-name</c>,
    /// <c>nodestate.gone</c>, <c>ec-keeper-apply</c>) for one condition, and five places to
    /// forget the try/catch.
    ///
    /// <b>One tag is right here, and only here.</b> <see cref="VgsLog"/> keys its throttle by
    /// tag and warns that two *different* call sites must never share one. These were never
    /// different sites: they are one condition — "this id does not resolve" — now asked once.
    ///
    /// <b>Absence is not logged.</b> A missing block returns false silently: the caller knows
    /// what it means for its own picture and says so in its own words (a position not saved, a
    /// card that is gone). Only an exception, which is a genuine surprise, reaches the log.
    ///
    /// Lives in <c>Vgs.Game</c> because that is the only layer allowed to touch the world. A
    /// caller in <c>Vgs.Ui</c> may use it from input handling, never from drawing (ADR-0003).
    /// </summary>
    public static class EntityResolver
    {
        /// <summary>
        /// Throttle identity of the lookup. One tag, because there is one call site now; see the
        /// type's remark on why the five tags it replaces were a mistake.
        /// </summary>
        private const string LogTag = "resolve-block";

        /// <summary>
        /// Calls per emitted log line while the lookup keeps throwing. Attempts, not frames: the
        /// editor pauses the game, so a frame-based throttle would freeze (ADR-0026).
        /// </summary>
        private const int ThrottleAttempts = 300;

        /// <summary>
        /// True when <paramref name="entityId"/> is a live terminal block, which is the only
        /// thing the editor ever draws a card for. A grid, a projectile or a destroyed block all
        /// answer false, and so does an id the game cannot read at all.
        /// </summary>
        public static bool TryResolveTerminalBlock(long entityId, out IMyTerminalBlock block)
        {
            block = null;

            try
            {
                block = MyAPIGateway.Entities.GetEntityById(entityId) as IMyTerminalBlock;
                return block != null;
            }
            catch (Exception e)
            {
                VgsLog.Throttled(LogTag, "could not resolve a block by entity id", e, ThrottleAttempts);
                return false;
            }
        }
    }
}
