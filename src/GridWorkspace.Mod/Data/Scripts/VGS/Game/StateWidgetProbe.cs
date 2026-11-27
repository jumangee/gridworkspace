using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Reads the one number a card's <b>state widget</b> draws (ADR-0042): how full a cargo
    /// container is, how charged a battery is, how far a timer has counted, how much a refinery or
    /// an assembler holds in each of its two inventories.
    ///
    /// <b>This type both decides the shape and reads it.</b> <see cref="KindOf"/> answers "which of
    /// the five is this block" from the <b>game's interfaces</b> — <c>IMyCargoContainer</c>,
    /// <c>IMyBatteryBlock</c>, <c>IMyTimerBlock</c>, <c>IMyRefinery</c>, <c>IMyAssembler</c> — and
    /// <see cref="TryRead"/> reads the numbers that shape needs. The two are separate methods
    /// because they run at different times: the shape is asked once, when the card is built, and the
    /// reading twice a second.
    ///
    /// <b>The category table is the fallback, not the source.</b> The node asks this type first and
    /// only consults <see cref="BlockPortCatalog.Descriptor"/> when the answer is
    /// <see cref="StateWidgetKind.None"/> (ADR-0042 §1, §7) — so a block with a recognised interface
    /// is read whatever its subtype says, and a block whose subtype resolves to nothing is still
    /// read if the game knows what it is. The reverse — subtype first — is what lost every cargo
    /// container: not one of the twenty is called "cargocontainer", and ten do not contain the word
    /// "container" at all.
    ///
    /// <b>Everything here is a boundary.</b> Each block is asked inside its own <c>try</c>: a block
    /// being destroyed mid-pass can throw from any of these members, and the pass runs over every
    /// visible card twice a second. A failed read returns false and the card keeps its previous
    /// picture — never a zero, which would claim "empty" about a container nobody managed to read.
    ///
    /// <b>Which API answers which question</b>, verified against the installed assemblies with
    /// <c>tools/dump-se-api.ps1</c> (see <c>docs/01-research/api-state-widget.md</c>):
    ///
    /// | Shape | Member | Type |
    /// |---|---|---|
    /// | cargo fill | <c>block.GetInventory(0).CurrentVolume / MaxVolume</c> | <c>IMyInventory</c>, <c>MyFixedPoint</c> |
    /// | battery charge | <c>IMyBatteryBlock.CurrentStoredPower / MaxStoredPower</c> | <c>Sandbox.ModAPI.Ingame.IMyBatteryBlock</c> |
    /// | battery mode | <c>IMyBatteryBlock.ChargeMode</c> | <c>Sandbox.ModAPI.Ingame.ChargeMode</c> |
    /// | timer progress | <c>MyObjectBuilder_TimerBlock.CurrentTime / Delay</c> | the block's own builder |
    /// | production fill | <c>IMyProductionBlock.InputInventory / OutputInventory</c> | <c>VRage.Game.ModAPI.IMyInventory</c> |
    /// </summary>
    public static class StateWidgetProbe
    {
        /// <summary>
        /// Which <b>shape</b> a block draws, decided from the <b>game's own interfaces</b> — not
        /// from its subtype name (ADR-0042 §7).
        ///
        /// <b>Why this is not the category table.</b> The first version of the widget took its shape
        /// from <see cref="BlockPortCatalog.Descriptor"/> of the category, and the category comes
        /// from a substring match on the subtype. That match knew no real cargo container: the game
        /// calls them <c>LargeBlockLargeContainer</c>, <c>SmallBlockSmallContainer</c>,
        /// <c>LargeBlockLargeIndustrialContainer</c> and so on — not one of them contains
        /// "cargocontainer", which is what the table was looking for. Every cargo container therefore
        /// fell through to <see cref="BlockCategory.Unknown"/>, and the card had no widget at all.
        /// Twenty subtypes in the shipped catalogue carry that typeId, and ten of them (three
        /// lockers, a bookshelf, two weapon racks, two cargo terminals, a lab cabinet and a desk) do
        /// not even contain the word "container" — so no substring rule could have covered them, and
        /// patching the table would only have moved the next such defect.
        ///
        /// The game already answers the question exactly: <c>IMyCargoContainer</c>,
        /// <c>IMyBatteryBlock</c>, <c>IMyTimerBlock</c>, <c>IMyRefinery</c> and <c>IMyAssembler</c>
        /// are what the block <em>is</em>. This is the same rule the project already applies to "can
        /// this block drive others" (<c>IMyToolbarProvider</c>, ADR-0011) and to "is this an event
        /// controller" (<c>IMyEventControllerBlock</c>, ADR-0017): ask the game, never the name.
        ///
        /// Returns <see cref="StateWidgetKind.None"/> for a block that is none of the five — which is
        /// most of them — and for a read that threw.
        /// </summary>
        public static StateWidgetKind KindOf(IMyTerminalBlock block)
        {
            if (block == null) return StateWidgetKind.None;

            try
            {
                // Most specific first. A refinery and an assembler are both IMyProductionBlock, so
                // the two are asked separately rather than through their common interface.
                if (block is IMyCargoContainer) return StateWidgetKind.FillBar;
                if (block is IMyBatteryBlock) return StateWidgetKind.Battery;
                if (block is SpaceEngineers.Game.ModAPI.IMyTimerBlock) return StateWidgetKind.Clock;
                if (block is IMyRefinery) return StateWidgetKind.Production;
                if (block is IMyAssembler) return StateWidgetKind.Production;

                return StateWidgetKind.None;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("widget-kind", "could not read what kind of block this is", e, 600);
                return StateWidgetKind.None;
            }
        }

        /// <summary>
        /// Reads the reading for a block of the given widget kind. False when the block cannot be
        /// read — gone, not the interface the kind needs, or an exception — and the caller then
        /// leaves the state alone.
        ///
        /// A returned reading with <see cref="WidgetReading.Active"/> false is a successful read of
        /// "there is nothing to show right now", which is a different answer from "I could not
        /// read": the first clears the widget, the second leaves it as it was.
        /// </summary>
        public static bool TryRead(IMyTerminalBlock block, StateWidgetKind kind,
                                   out WidgetReading reading)
        {
            reading = WidgetReading.None;
            if (block == null) return false;

            switch (kind)
            {
                case StateWidgetKind.FillBar:
                    return TryReadFill(block, out reading);

                case StateWidgetKind.Battery:
                    return TryReadBattery(block, out reading);

                case StateWidgetKind.Clock:
                    return TryReadClock(block, out reading);

                case StateWidgetKind.Production:
                    return TryReadProduction(block, out reading);

                default:
                    return false;
            }
        }

        /// <summary>
        /// A cargo container's occupancy: its first inventory's volume against its maximum.
        ///
        /// <b>The two volumes, not <c>VolumeFillFactor</c>.</b> The game's own factor returns 0 when
        /// the inventory is not constrained (read from <c>MyInventory.get_VolumeFillFactor</c> by IL),
        /// which is a different question — "is this inventory bounded" — and would paint an unbounded
        /// inventory as empty. Dividing the two volumes asks the question the card asks, and it
        /// answers for every inventory that has a maximum at all.
        /// </summary>
        private static bool TryReadFill(IMyTerminalBlock block, out WidgetReading reading)
        {
            reading = WidgetReading.None;

            try
            {
                IMyInventory inventory = block.GetInventory(0);
                if (inventory == null) return false;

                float fill;
                if (!TryFillOf(inventory, out fill)) return false;

                reading = new WidgetReading(true, fill, 0f, WidgetAccent.None);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("widget-fill", "could not read the container's fill", e, 600);
                return false;
            }
        }

        /// <summary>
        /// A battery's charge and the direction it is moving in.
        ///
        /// <b>The mode is the game's <c>ChargeMode</c>, not a guess from the numbers.</b> Comparing
        /// the stored power against its previous reading would need the previous reading kept
        /// somewhere and would report "discharging" for a battery that is merely full and idle.
        /// The game already knows the answer: <c>Auto</c> is neither direction, <c>Recharge</c> and
        /// <c>Discharge</c> are the two the requirement asked to draw.
        /// </summary>
        private static bool TryReadBattery(IMyTerminalBlock block, out WidgetReading reading)
        {
            reading = WidgetReading.None;

            try
            {
                var battery = block as Sandbox.ModAPI.Ingame.IMyBatteryBlock;
                if (battery == null) return false;

                float max = battery.MaxStoredPower;
                if (max <= 0f) return false;

                float charge = battery.CurrentStoredPower / max;

                WidgetAccent accent = WidgetAccent.None;
                switch (battery.ChargeMode)
                {
                    case Sandbox.ModAPI.Ingame.ChargeMode.Recharge:
                        accent = WidgetAccent.Recharge;
                        break;

                    case Sandbox.ModAPI.Ingame.ChargeMode.Discharge:
                        accent = WidgetAccent.Discharge;
                        break;
                }

                reading = new WidgetReading(true, charge, 0f, accent);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("widget-battery", "could not read the battery's charge", e, 600);
                return false;
            }
        }

        /// <summary>
        /// A timer's progress towards firing: how much of its delay has elapsed.
        ///
        /// <b>The remaining time is not on the interface, and the builder is.</b>
        /// <c>IMyTimerBlock</c> exposes <c>IsCountingDown</c>, <c>TriggerDelay</c> and the three
        /// commands, and nothing that says how far the countdown has got. The block's own object
        /// builder does: <c>GetObjectBuilderCubeBlock</c> writes <c>CurrentTime = m_countdownMsCurrent</c>
        /// and <c>Delay = m_countdownMsStart</c> (read from the IL of
        /// <c>MyTimerBlock.GetObjectBuilderCubeBlock</c>), which is the same read the event controller
        /// probe already makes for its watch list.
        ///
        /// <b>Not counting down means nothing to show.</b> A stopped timer's progress is not zero —
        /// it does not exist — so the reading is inactive and the card draws no widget at all. That
        /// is the requirement's own wording: the widget is empty while the block is not counting.
        /// </summary>
        private static bool TryReadClock(IMyTerminalBlock block, out WidgetReading reading)
        {
            reading = WidgetReading.None;

            try
            {
                var timer = block as SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock;
                if (timer == null) return false;
                if (!timer.IsCountingDown) return true;   // read successfully: nothing to show

                var builder = block.GetObjectBuilderCubeBlock(false)
                              as Sandbox.Common.ObjectBuilders.MyObjectBuilder_TimerBlock;
                if (builder == null) return false;

                int delay = builder.Delay;
                if (delay <= 0) return false;

                int current = builder.CurrentTime;
                if (current < 0) current = 0;
                if (current > delay) current = delay;

                // CurrentTime is what is LEFT of the countdown — `Start` sets it to the full delay,
                // and `UpdateAfterSimulation10` subtracts 166 ms per call (about six calls a second
                // at 60 ticks) — so the elapsed share is the complement. Elapsed, not remaining: the
                // hand starts at twelve and comes back to twelve when the timer fires, which is the
                // picture the requirement describes (ADR-0042 §4).
                //
                // The "ten milliseconds per tick" this comment used to claim was wrong; the IL of
                // `UpdateAfterSimulation10` carries `ldc.i4 0xA6` = 166, and the correction is
                // recorded in docs/01-research/api-state-widget.md §4 rather than made silently.
                float elapsed = 1f - (float)current / delay;

                reading = new WidgetReading(true, elapsed, 0f, WidgetAccent.None);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("widget-timer", "could not read the timer's countdown", e, 600);
                return false;
            }
        }

        /// <summary>
        /// A refinery's or an assembler's two inventories: what goes in, what comes out.
        ///
        /// <b>Both are read in one try, and one failing read costs the pair.</b> The two bars are one
        /// widget: drawing the input from a fresh reading beside an output from a stale one would
        /// show a block that is simultaneously full and empty. A failed read leaves the previous
        /// pair intact, which is at worst half a second old.
        /// </summary>
        private static bool TryReadProduction(IMyTerminalBlock block, out WidgetReading reading)
        {
            reading = WidgetReading.None;

            try
            {
                var production = block as Sandbox.ModAPI.IMyProductionBlock;
                if (production == null) return false;

                float input, output;
                if (!TryFillOf(production.InputInventory, out input)) return false;
                if (!TryFillOf(production.OutputInventory, out output)) return false;

                reading = new WidgetReading(true, input, output, WidgetAccent.None);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("widget-production", "could not read the production inventories", e, 600);
                return false;
            }
        }

        /// <summary>
        /// One inventory's occupancy as a fraction 0..1. False when the inventory has no maximum
        /// (an unlimited one), where the question the bar asks has no answer.
        ///
        /// The conversion goes through <c>float</c> because <c>MyFixedPoint</c> has an explicit
        /// conversion operator to it — the same one the game's own <c>VolumeFillFactor</c> uses.
        /// </summary>
        private static bool TryFillOf(IMyInventory inventory, out float fill)
        {
            fill = 0f;
            if (inventory == null) return false;

            float max = (float)inventory.MaxVolume;
            if (max <= 0f) return false;

            float current = (float)inventory.CurrentVolume;
            if (current < 0f) current = 0f;

            fill = current / max;
            if (fill > 1f) fill = 1f;

            return true;
        }
    }
}
