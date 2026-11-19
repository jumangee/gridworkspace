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
    /// Injects the "Grid setup" terminal button into cockpit blocks and routes the click to
    /// the editor screen.
    ///
    /// How Space Engineers terminal controls work, and the consequences:
    ///
    ///  * Controls are registered <b>per block type</b>, not per block instance. One
    ///    registration affects every cockpit in the world, including other players' ships.
    ///    There is no per-instance control, so anything instance-specific must be decided
    ///    inside the control's callbacks (Visible / Enabled / Action).
    ///
    ///  * The block type must already exist when we register. Registering during
    ///    <c>Init()</c> is too early — the game is still building its definition tables and
    ///    the control can be dropped. The supported pattern is to register once the session
    ///    is running, which <see cref="VgsSessionComponent"/> does in its first update.
    ///
    ///  * Registration is client-side UI only. It does not synchronise, does not persist,
    ///    and is not visible to dedicated servers. That is fine here: the button only opens
    ///    a local screen.
    ///
    ///  * <c>CustomControlGetter</c> is the supported hook for adding a control to a block
    ///    type we do not own. It fires for every terminal block the player inspects, so it
    ///    must be cheap and must never throw.
    /// </summary>
    public static class VgsTerminalControls
    {
        private static bool _registered;
        private static IMyTerminalControlButton _button;

        /// <summary>
        /// Registers the button once. Safe to call repeatedly.
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

                MyAPIGateway.TerminalControls.AddControl<IMyCockpit>(_button);
                MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;

                _registered = true;
                VgsLog.Info("terminal control registered: " + VgsConstants.TerminalButtonId);
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to register terminal control", e);
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
                }
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to unregister terminal control", e);
            }
            finally
            {
                _button = null;
                _registered = false;
            }
        }

        /// <summary>
        /// Hook that appends our button to the control list of any cockpit. Runs for every
        /// terminal block the player opens, so it stays allocation-free and guarded.
        /// </summary>
        private static void OnCustomControlGetter(Sandbox.ModAPI.IMyTerminalBlock block,
                                                  List<IMyTerminalControl> controls)
        {
            try
            {
                if (_button == null || controls == null) return;

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

