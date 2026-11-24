using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Vgs.Core;

namespace Vgs.Game
{
    /// <summary>
    /// Reads and writes the one thing the card's power switch acts on: a block's own on/off flag
    /// (ADR-0035).
    ///
    /// <b>Why a type and not three lines in the click handler.</b> Three different questions are
    /// asked of the game here, and each has a wrong answer that compiles:
    ///
    ///  * <i>does this block have a switch at all</i> — <c>block is IMyFunctionalBlock</c>. A
    ///    conveyor tube does not, and a card that offered it a switch would offer a control that
    ///    can never do anything.
    ///  * <i>can it be switched on</i> — <c>IsFunctional</c>, which the game computes as
    ///    <c>IsBuilt &amp;&amp; Integrity &gt; MaxIntegrity * CriticalIntegrityRatio</c> (read from
    ///    <c>MyComponentStack.get_IsFunctional</c> by IL, see
    ///    <c>docs/01-research/api-block-state.md</c>). Note what it is <b>not</b>: it is not
    ///    <c>Enabled</c>, and it is not a threshold this mod invents.
    ///  * <i>is it on</i> — <c>Enabled</c>.
    ///
    /// Answering them in one place is what keeps the draw path, the hit test and the click from
    /// disagreeing about which of the three states a card is in.
    ///
    /// <b>The write goes through the <c>Enabled</c> setter, not through <c>RequestEnable</c>.</b>
    /// Both end in the same place — <c>RequestEnable</c>'s IL body is
    /// <c>ldarg.1; callvirt set_Enabled</c>, and the game marks the method obsolete with "use the
    /// setter of Enabled" — so the supported member is the one used here.
    /// <c>Enabled</c> is a <c>Sync&lt;bool, BothWays&gt;</c>, so on a remote client the value is
    /// applied locally and sent to the server, which may refuse it. That refusal is not visible
    /// here; the state pass (~2 Hz) reads the block again and the card follows the truth, which is
    /// the same "the game is the source of truth" rule the watch list follows (ADR-0018). Saying
    /// "sent" and letting the picture correct itself is honest; pretending a client-side write is
    /// final would not be.
    ///
    /// Everything here is a boundary: it is called from a click handler and from the state pass,
    /// and neither may see an exception.
    /// </summary>
    public static class BlockSwitch
    {
        /// <summary>
        /// True when the block has an on/off flag of its own. False for a block that has no
        /// switch, for a block that is gone, and for a read that threw — the caller draws no
        /// switch in all three cases, which is the honest answer rather than a control that does
        /// nothing.
        /// </summary>
        public static bool CanBeEnabled(IMyTerminalBlock block)
        {
            if (block == null) return false;

            try
            {
                return block is IMyFunctionalBlock;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("switch-kind", "could not read whether the block has a switch", e, 600);
                return false;
            }
        }

        /// <summary>
        /// Reads the two facts the switch shows. False when either cannot be read, and the caller
        /// then keeps the previous picture: one failed read is not evidence that the block was
        /// switched, exactly as with the integrity reading.
        /// </summary>
        public static bool TryRead(IMyTerminalBlock block, out bool enabled, out bool functional)
        {
            enabled = false;
            functional = true;
            if (block == null) return false;

            try
            {
                var functionalBlock = block as IMyFunctionalBlock;
                if (functionalBlock == null) return false;

                enabled = functionalBlock.Enabled;

                // IsFunctional lives on the cube block and answers "would the game let this run
                // at all", which is the question behind the red switch-off. Reading it after
                // Enabled keeps the two in one try: a block that is being destroyed can throw on
                // either, and half a reading is worse than none.
                functional = block.IsFunctional;
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("switch-read", "could not read the block's switch", e, 600);
                return false;
            }
        }

        /// <summary>
        /// Asks the block to switch itself on or off and reports whether the flag now reads as
        /// requested. False means the block did not accept it — gone, no switch, or a server that
        /// refused — and the caller must say so rather than paint a state that did not happen.
        ///
        /// The read-back is what makes the answer mean something. The setter returns nothing, so
        /// the only evidence that the block changed is the flag itself.
        /// </summary>
        public static bool TrySetEnabled(IMyTerminalBlock block, bool enabled)
        {
            if (block == null) return false;

            try
            {
                var functionalBlock = block as IMyFunctionalBlock;
                if (functionalBlock == null) return false;

                // The property setter, not `RequestEnable`: the game marks the method obsolete and
                // points at the setter, and the two do the same thing anyway — `RequestEnable`'s
                // IL body is `ldarg.1; callvirt set_Enabled` (read with `dump-se-il.ps1`). Using
                // the supported member keeps the mod compiling without a deprecation warning that
                // would hide the next, real one.
                functionalBlock.Enabled = enabled;

                // A block that is not functional reports Enabled but does not run; switching it
                // "on" is therefore not a success even when the flag took. The caller checks the
                // functional flag separately and refuses before it gets here, so this is only a
                // backstop for a block that broke between the two reads.
                return functionalBlock.Enabled == enabled;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("switch-write", "could not switch the block", e, 300);
                return false;
            }
        }
    }
}
