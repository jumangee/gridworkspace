using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI;
using Vgs.Core;
using Vgs.Model;
using IMyMotorStator = Sandbox.ModAPI.IMyMotorStator;
using IMyProgrammableBlock = Sandbox.ModAPI.IMyProgrammableBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace Vgs.Game
{
    /// <summary>
    /// The starting values of an action's arguments, read from the block the connection is being
    /// made to (ADR-0051).
    ///
    /// <b>Why the block, and not a constant.</b> The game's own dialogs open pre-filled: the
    /// rotor's speed dialog starts at the rotor's maximum, and the programmable block's run
    /// argument starts at the string it ran last — the game even offers a second action,
    /// <c>RunWithDefaultArgument</c>, that exists only because that value is worth reusing. A form
    /// that opens empty makes the player retype what the block already knows.
    ///
    /// <b>What is read, and in what unit.</b> Every property below is a documented member of the
    /// ModAPI interface, and the units are the ones the action's handler expects — verified by IL,
    /// see <c>docs/01-research/api-action-parameters.md</c> §5:
    ///
    ///  * <c>IMyProgrammableBlock.TerminalRunArgument</c> — the string the script last received;
    ///  * <c>IMyMotorStator.TargetVelocityRPM</c> — RPM, which is what <c>RotateToAngle</c> and
    ///    <c>SetVelocity</c> take;
    ///  * <c>IMyMotorStator.LowerLimitDeg</c> / <c>UpperLimitDeg</c> — degrees (the getter calls
    ///    <c>MathHelper.ToDegrees</c>), which is what the limit actions take.
    ///
    /// <b>Every read is a boundary.</b> The block may be destroyed, may be a group's member that
    /// no longer exists, or may simply not implement the interface. None of that is an error: the
    /// answer is "no default for this field", the field opens empty, and the form's own validation
    /// still decides what may be confirmed.
    /// </summary>
    public static class ActionParameterDefaults
    {
        /// <summary>
        /// The starting values for an action's arguments, with an empty entry wherever there is no
        /// default. <see cref="ParameterSet"/> keeps the positions, so the form can line the values
        /// up with its fields.
        /// </summary>
        public static ParameterSet For(IMyTerminalBlock block, string actionKey, IList<TypeCode> declared)
        {
            ParameterSpec[] specs = ActionParameterHints.For(actionKey, declared, VgsText.Language);
            if (specs.Length == 0) return ParameterSet.None;

            try
            {
                var items = new ActionParameter[specs.Length];
                bool any = false;

                for (int i = 0; i < specs.Length; i++)
                {
                    string text = ValueOf(block, specs[i]);
                    if (text == null) continue;

                    ActionParameter parameter;
                    ParameterError error;
                    if (!specs[i].TryValidate(text, out parameter, out error)) continue;

                    items[i] = parameter;
                    any = true;
                }

                return any ? ParameterSet.Of(items) : ParameterSet.None;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("parameter-defaults", "could not read an argument's default", e, 300);
                return ParameterSet.None;
            }
        }

        /// <summary>
        /// The value one argument starts at, as the text the form holds, or null when the block
        /// cannot supply one.
        /// </summary>
        private static string ValueOf(IMyTerminalBlock block, ParameterSpec spec)
        {
            switch (spec.DefaultFrom)
            {
                case ParameterDefault.Literal:
                    return spec.DefaultText;

                case ParameterDefault.ProgrammableBlockArgument:
                {
                    IMyProgrammableBlock pb = block as IMyProgrammableBlock;
                    return pb == null ? null : pb.TerminalRunArgument;
                }

                case ParameterDefault.MotorVelocity:
                {
                    IMyMotorStator motor = block as IMyMotorStator;
                    return motor == null ? null : Number(motor.TargetVelocityRPM);
                }

                case ParameterDefault.MotorLowerLimit:
                {
                    IMyMotorStator motor = block as IMyMotorStator;
                    return motor == null ? null : Number(motor.LowerLimitDeg);
                }

                case ParameterDefault.MotorUpperLimit:
                {
                    IMyMotorStator motor = block as IMyMotorStator;
                    return motor == null ? null : Number(motor.UpperLimitDeg);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// A float as the invariant text the toolbar stores. Rounded to three decimals: the block's
        /// velocity is a measured quantity, and "12.000001" in a field the player is about to
        /// confirm reads as noise rather than as a value.
        /// </summary>
        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return null;
            return Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
        }
    }
}

