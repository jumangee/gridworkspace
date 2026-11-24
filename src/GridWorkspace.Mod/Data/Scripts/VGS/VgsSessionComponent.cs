using System;
using VRage.Game;
using VRage.Game.Components;
using Vgs.Core;
using Vgs.Game;
using Vgs.Ui;

namespace Vgs
{
    /// <summary>
    /// The mod's single entry point.
    ///
    /// Space Engineers discovers session components by scanning mod assemblies for types
    /// carrying <see cref="MySessionComponentDescriptorAttribute"/>; the game then constructs
    /// and drives them. There is no other hook, which is why this class exists and why it
    /// stays thin: it owns lifecycle and delegates everything else.
    ///
    /// Ordering that matters (verified against the game's own update loop):
    ///  * <see cref="Init"/> runs before the world is fully loaded — terminal block types may
    ///    not be registered yet, so terminal controls must NOT be created here.
    ///  * <see cref="BeforeStart"/> runs once the session is starting.
    ///  * <see cref="UpdateAfterSimulation"/> runs every tick after the physics step; it is
    ///    the correct place for the deferred, one-shot registration.
    ///  * <see cref="UnloadData"/> must remove every global hook, or the hooks keep pointing
    ///    at a dead session component when the player leaves to the main menu and back.
    ///
    /// Every override is guarded: an exception escaping one of these breaks the session
    /// update, not just this mod.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class VgsSessionComponent : MySessionComponentBase
    {
        private bool _initialised;
        private int _registrationAttempts;

        /// <summary>
        /// True once the game's language has been read successfully (ADR-0033).
        ///
        /// It has to be resolved <b>before</b> the terminal controls are created: their titles and
        /// tooltips are resolved from <c>MyStringId</c> at registration, so a table switched
        /// afterwards would leave the button in the old language until the world was reloaded.
        /// </summary>
        private bool _languageResolved;

        /// <summary>Guards against registering more than a few times if the API is late.</summary>
        private const int MaxRegistrationAttempts = 600;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            try
            {
                base.Init(sessionComponent);

                VgsSession.Bind(this);
                _initialised = true;

                VgsLog.Info("session component initialised (mod " + VgsConstants.ModGuid + ")");
            }
            catch (Exception e)
            {
                // A failure here leaves _initialised false, so every later override returns
                // early and the mod stays dormant instead of throwing on every tick.
                VgsLog.Error("Init failed", e);
            }
        }

        public override void BeforeStart()
        {
            try
            {
                base.BeforeStart();

                if (!_initialised) return;
                VgsLog.Info("session starting");
            }
            catch (Exception e)
            {
                VgsLog.Error("BeforeStart failed", e);
            }
        }

        public override void UpdateAfterSimulation()
        {
            if (!_initialised) return;

            try
            {
                // The keeper replays an event controller's saved watch list into the live block
                // once the block exists (ADR-0017). It is installed lazily because the entity
                // system is not guaranteed to be up in Init, and it runs every tick because a
                // controller can be built at any moment.
                EventControllerKeeper.Install();
                EventControllerKeeper.Update();
            }
            catch (Exception e)
            {
                VgsLog.Throttled("keeper", "event controller keeper failed", e, 300);
            }

            try
            {
                // Deferred registration: the terminal control factory is only usable once the
                // game has created its block definitions. Doing this in Init() silently loses
                // the control on some load orders, so we retry here until it sticks.
                if (_registrationAttempts >= MaxRegistrationAttempts) return;

                _registrationAttempts++;

                // The editor speaks the game's language (ADR-0033). Resolved **here**, before the
                // controls are created, because MyStringId bakes a title into a control at
                // registration: a table switched afterwards would leave the button in the old
                // language until the world was reloaded.
                //
                // Bounded by the same counter as the registration. Without that, a session whose
                // config never appears would cost a property read and a try block on every tick
                // for the rest of the world's life — and the editor re-reads the language when it
                // opens anyway, so giving up here costs nothing the player can see.
                if (!_languageResolved)
                {
                    try
                    {
                        _languageResolved = GameLanguage.Apply();
                    }
                    catch (Exception e)
                    {
                        // Apply is guarded internally; this catch exists because the rule at this
                        // boundary is unconditional, and a future change there must not be able
                        // to break the session's update. Tag distinct from Apply's own, so one
                        // broken path cannot silence the other.
                        VgsLog.Throttled("language-apply", "language resolution failed", e, 300);
                    }
                }

                if (VgsSession.IsDedicated)
                {
                    // A dedicated server has no GUI; the button would be dead weight and
                    // CustomControlGetter is never called.
                    _registrationAttempts = MaxRegistrationAttempts;
                    return;
                }

                VgsTerminalControls.EnsureRegistered();
            }
            catch (Exception e)
            {
                // Throttled: if this throws every frame, an unthrottled log would bury the
                // rest of the session log within seconds.
                VgsLog.Throttled("update", "UpdateAfterSimulation failed", e, 300);
            }
        }

        protected override void UnloadData()
        {
            // The screen first: it holds a generated texture and, if it is still open, the
            // game will otherwise keep drawing it against a session that no longer exists.
            try
            {
                VgsEditorScreen.CloseIfOpen();
                VgsScreenDraw.Release();
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to close the editor screen", e);
            }

            try
            {
                EventControllerKeeper.Uninstall();
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to uninstall the event controller keeper", e);
            }

            try
            {
                // The editor's paste buffer is session state: leaving it behind would hand the
                // next world whatever was copied in the previous one (ADR-0024).
                TextClipboard.Release();
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to release the text clipboard buffer", e);
            }

            try
            {
                VgsTerminalControls.Unregister();
            }
            catch (Exception e)
            {
                VgsLog.Error("failed to unregister terminal controls", e);
            }

            try
            {
                VgsLog.Info("session component unloaded");
            }
            catch
            {
                // Intentionally ignored: the log is already gone at this point in teardown.
            }

            // Reset last, so the two calls above still have their throttle state.
            VgsSession.Reset();
            VgsLog.Reset();
            VgsText.Reset();
            _languageResolved = false;
            _initialised = false;

            try
            {
                base.UnloadData();
            }
            catch (Exception e)
            {
                VgsLog.Error("base UnloadData failed", e);
            }
        }
    }
}
