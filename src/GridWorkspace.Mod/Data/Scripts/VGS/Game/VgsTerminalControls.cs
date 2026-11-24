using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using Vgs.Core;
using Vgs.Ui;

namespace Vgs.Game
{
    /// <summary>
    /// Injects the mod's terminal controls and routes them:
    ///
    ///  * <b>"Grid setup"</b> — a button on cockpits that opens the editor;
    ///  * <b>"Show in VGS"</b> — a checkbox on every terminal block that hides the block from
    ///    the schema (ADR-0013).
    ///
    /// How Space Engineers terminal controls work, and the consequences:
    ///
    ///  * Controls are registered <b>per block type</b>, not per block instance. One
    ///    registration affects every cockpit in the world, including other players' ships.
    ///    There is no per-instance control, so anything instance-specific must be decided
    ///    inside the control's callbacks (Visible / Enabled / Action / Getter / Setter).
    ///
    ///  * The block type must already exist when we register. Registering during
    ///    <c>Init()</c> is too early — the game is still building its definition tables and
    ///    the control can be dropped. The supported pattern is to register once the session
    ///    is running, which <see cref="VgsSessionComponent"/> does in its first update.
    ///
    ///  * <b>A control added to a base type does not reach types that were initialised
    ///    earlier.</b> <c>MyTerminalControlFactory</c> keeps one control list per type and
    ///    copies a base type's list into a derived type exactly once, in
    ///    <c>InitializeControls</c>. Since the game initialises block types as it loads them,
    ///    <c>AddControl&lt;IMyTerminalBlock&gt;</c> alone would give the checkbox to an
    ///    unpredictable subset of blocks. <c>CustomControlGetter</c> is therefore the primary
    ///    path: it fires for the concrete block being inspected and hands us the list to
    ///    append to.
    ///
    ///  * Registration is client-side UI only. It does not synchronise, does not persist,
    ///    and is not visible to dedicated servers. That is fine here: the controls only open a
    ///    local screen and write a per-block flag.
    ///
    ///  * <c>CustomControlGetter</c> fires for every terminal block the player inspects, so it
    ///    must be cheap and must never throw.
    /// </summary>
    public static class VgsTerminalControls
    {
        private static bool _registered;
        private static IMyTerminalControlButton _button;
        private static IMyTerminalControlCheckbox _checkbox;

        /// <summary>
        /// Registers the controls once. Safe to call repeatedly.
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;

            try
            {
                if (MyAPIGateway.TerminalControls == null)
                {
                    // Terminal API is not available yet; the caller retries next frame.
                    return;
                }

                _button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCockpit>(
                    VgsConstants.TerminalButtonId);

                _button.Title = MyStringId.GetOrCompute(VgsText.ButtonTitle);
                _button.Tooltip = MyStringId.GetOrCompute(VgsText.ButtonTooltip);

                // Only offer the button to players who may actually operate the block.
                // Showing it on a hostile grid would be misleading: the editor reads blocks
                // the player cannot access.
                _button.Enabled = CanOpenEditor;
                _button.Visible = CanSeeEditor;

                _button.Action = OnButtonPressed;

                CreateCheckbox();

                MyAPIGateway.TerminalControls.AddControl<IMyCockpit>(_button);

                // The checkbox is wanted on every terminal block. This registration covers the
                // types the game initialises after us; the getter covers all the others, and is
                // the path that actually matters (see the class comment).
                MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(_checkbox);

                MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;

                _registered = true;
                VgsLog.Info("terminal control registered: " + VgsConstants.TerminalButtonId
                            + ", " + VgsConstants.ShowInVgsControlId);
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to register terminal control", e);
            }
        }

        /// <summary>
        /// Creates the "Show in VGS" checkbox. <c>Visible</c> and <c>Enabled</c> are left at
        /// their defaults on purpose: the control constructor installs delegates that return
        /// <c>true</c>, and <c>ITerminalControl.IsVisible</c> calls that delegate without a
        /// null check — assigning null would break the terminal panel.
        /// </summary>
        private static void CreateCheckbox()
        {
            _checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyTerminalBlock>(
                VgsConstants.ShowInVgsControlId);

            _checkbox.Title = MyStringId.GetOrCompute(VgsText.ShowInVgsTitle);
            _checkbox.Tooltip = MyStringId.GetOrCompute(VgsText.ShowInVgsTooltip);
            _checkbox.OnText = MyStringId.GetOrCompute(VgsText.ShowInVgsOn);
            _checkbox.OffText = MyStringId.GetOrCompute(VgsText.ShowInVgsOff);

            // Method groups, not lambdas: the getter runs on every repaint of the terminal
            // panel, and a captured lambda would be an allocation per control instance for no
            // gain. Both methods are allocation-free by design (one dictionary lookup).
            _checkbox.Getter = NodeStateStore.IsShown;
            _checkbox.Setter = OnShowInVgsChanged;
        }

        /// <summary>
        /// Stores the flag and refreshes the control, so the tick matches the stored value
        /// immediately rather than on the next panel rebuild.
        /// </summary>
        private static void OnShowInVgsChanged(IMyTerminalBlock block, bool shown)
        {
            try
            {
                NodeStateStore.SetShown(block, shown);
                if (_checkbox != null) _checkbox.UpdateVisual();
            }
            catch (Exception e)
            {
                VgsLog.Throttled("showinvgs", "could not store the visibility flag", e, 300);
            }
        }

        /// <summary>Removes the registration. Called on session unload.</summary>
        public static void Unregister()
        {
            if (!_registered) return;

            try
            {
                if (MyAPIGateway.TerminalControls != null)
                {
                    MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;

                    if (_button != null) MyAPIGateway.TerminalControls.RemoveControl<IMyCockpit>(_button);
                    if (_checkbox != null) MyAPIGateway.TerminalControls.RemoveControl<IMyTerminalBlock>(_checkbox);
                }
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to unregister terminal control", e);
            }
            finally
            {
                _button = null;
                _checkbox = null;
                _registered = false;
            }
        }

        /// <summary>
        /// Hook that appends our controls to the control list of the block being inspected.
        /// Runs for every terminal block the player opens, so it stays allocation-free and
        /// guarded.
        ///
        /// The checkbox goes on every block; the button stays cockpit-only.
        /// </summary>
        private static void OnCustomControlGetter(Sandbox.ModAPI.IMyTerminalBlock block,
                                                  List<IMyTerminalControl> controls)
        {
            try
            {
                if (block == null || controls == null) return;

                if (_checkbox != null && !controls.Contains(_checkbox)) controls.Add(_checkbox);

                if (_button == null) return;

                var cockpit = block as IMyCockpit;
                if (cockpit == null) return;

                if (!controls.Contains(_button)) controls.Add(_button);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("controlgetter", "CustomControlGetter failed", e, 600);
            }
        }

        private static bool CanSeeEditor(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            // Never offer the editor where there is no GUI to show it in.
            return VgsSession.HasLocalPlayer;
        }

        private static bool CanOpenEditor(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            if (!VgsSession.HasLocalPlayer) return false;

            try
            {
                var cockpit = block as IMyCockpit;
                if (cockpit == null) return false;

                // Same access rule the game applies to terminal actions: the player must be
                // allowed to interact with the block.
                var player = MyAPIGateway.Session.Player;
                if (player == null) return false;

                long identityId = player.IdentityId;
                if (identityId == 0L) return true;

                return cockpit.HasPlayerAccess(identityId);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("accesscheck", "access check failed", e, 600);
                return false;
            }
        }

        private static void OnButtonPressed(Sandbox.ModAPI.IMyTerminalBlock block)
        {
            try
            {
                if (!VgsSession.HasLocalPlayer)
                {
                    VgsLog.Info("editor requested but no local player (server/headless)");
                    return;
                }

                var cockpit = block as IMyCockpit;
                if (cockpit == null) return;

                VgsEditorScreen.OpenFor(cockpit);
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to open editor screen", e);
                try
                {
                    MyAPIGateway.Utilities.ShowNotification(VgsText.ErrOpenScreen, 4000);
                }
                catch
                {
                    // Intentionally ignored: if the HUD notification fails too, the error is
                    // already in the log and there is no second channel left to report on.
                }
            }
        }
    }
}
