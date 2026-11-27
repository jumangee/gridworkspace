using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using Vgs.Core;
using Vgs.Ui;

namespace Vgs.Game
{
    /// <summary>
    /// The <b>M</b> hotkey: opens the schema from the cockpit the player is sitting in.
    ///
    /// <b>Why this exists.</b> The terminal button is the only way in, and reaching it costs a
    /// keyboard round trip: open the terminal, find the cockpit's panel, press the button, then
    /// close the terminal again on the way out. A player who is flying and wants to look at the
    /// ship's logic is in the cockpit already, so the schema should be one key away. The hotkey is
    /// that key, and it opens <b>the same screen</b> as the button — same entry point
    /// (<see cref="VgsEditorScreen.OpenFor"/>), same stored view, same everything after it.
    ///
    /// <b>Only from a cockpit, and that is the whole point.</b> The schema is a picture of a
    /// construct's terminal system; a player on foot, in a character or in a spectator camera has
    /// no construct to look at, and opening an empty or unrelated grid would be a puzzle rather
    /// than a feature. The test is the same set of blocks the terminal button is registered on:
    /// <see cref="IMyCockpit"/>. "Controlling a cockpit" is read from the session
    /// (<c>IMySession.ControlledObject</c>), which is exactly "which block is the player in",
    /// verified by IL — see <c>docs/01-research/api-hotkey.md</c>.
    ///
    /// <b>Only when nothing else owns the keyboard.</b> A hotkey that fires while the player is
    /// typing is a bug, not a shortcut: the terminal has a search box, chat has a text field, and
    /// menus are driven by the same keys. The screen checks below are the game's own answer to
    /// "is another screen open":
    ///
    ///  * <see cref="IMyGui.ChatEntryVisible"/> — the player is typing a chat message;
    ///  * <see cref="IMyGui.ActiveGamePlayScreen"/> — a gameplay screen is on top (help, the
    ///    toolbar config, progression), <b>or the terminal</b>: the accessor returns
    ///    <c>"MyGuiScreenTerminal"</c> when the terminal screen is up, so this one check already
    ///    covers it;
    ///  * <see cref="IMyGui.GetCurrentScreen"/> — the terminal page, asked of the terminal screen
    ///    itself. <b>Kept although the field above already answers for the terminal</b>, for two
    ///    reasons: it is the same question <see cref="TerminalJump"/> asks (ADR-0021), so the mod
    ///    has one way to ask it; and it does not depend on the field being written by the input
    ///    handler, which is where the coverage of that field comes from and its limit too;
    ///  * <see cref="VgsEditorScreen.IsEditorOpen"/> — the editor itself is already up, and a
    ///    second open request would only be a no-op (ADR-0005).
    ///
    /// The limit of the first of those checks is recorded rather than hidden: the game writes
    /// that field in one place only, its own screen-opening input handler, so it means "no screen
    /// was opened that way". A screen opened by other code — the multiplayer pause menu, another
    /// mod's screen — is not covered. In single player the question does not arise, because a
    /// paused game stops the session's update before this poll runs at all; in multiplayer the
    /// editor may open over such a screen and is closed with Esc. Accepted in ADR-0045.
    ///
    /// <b>Why the key is polled and not registered.</b> The mod API has no way to add a control
    /// binding: the game's control scheme is read from its config at load and there is no
    /// <c>AddControl</c> on <c>IMyInput</c> — only getters. So the key is polled once per
    /// simulation tick from <see cref="VgsSessionComponent"/> and resolved with
    /// <c>IsNewKeyPressed</c>, the same call the editor's own search field uses (ADR-0024). The
    /// poll is a handful of property reads that return early on the first false, so it costs
    /// nothing on the ticks where the player is not pressing anything.
    ///
    /// <b>Nothing here throws.</b> This runs from the session's update, which the game calls: an
    /// exception would break the session, so the whole poll is guarded and every failure becomes
    /// one throttled log line.
    /// </summary>
    public static class EditorHotkey
    {
        /// <summary>
        /// The key that opens the editor.
        ///
        /// Deliberately <b>not</b> in <see cref="VgsConstants"/>: that class is referenced from
        /// <c>Vgs.Model</c> (the view format reads its bounds), and <c>MyKeys</c> lives in
        /// <c>VRage.Input</c>, which the model layer must not need — <c>tests/OfflineChecks.cs</c>
        /// compiles <c>Vgs.Model</c> without the game assemblies, so a game enum there would break
        /// the offline gate rather than the game. A key is not a layout number either: it is the
        /// one fact this type exists to hold.
        /// </summary>
        private const VRage.Input.MyKeys OpenKey = VRage.Input.MyKeys.M;

        /// <summary>
        /// Reads the keyboard once and opens the editor if the player asked for it. Called from the
        /// session's update every simulation tick; cheap and silent when the key is not pressed.
        /// </summary>
        public static void Poll()
        {
            try
            {
                // No player, no screen: a dedicated server never reaches the code below, and a
                // client that is still loading has no one to show a schema to.
                if (!VgsSession.HasLocalPlayer) return;

                // Already open: the key would only ask for a screen that exists (ADR-0005). This
                // check is first because it is the one that is true for as long as the player
                // reads the schema, which is the longest stretch of frames this method runs.
                if (VgsEditorScreen.IsEditorOpen) return;

                VRage.ModAPI.IMyInput input = MyAPIGateway.Input;
                if (input == null) return;

                // "New press", not "held": holding M must not reopen the editor the moment the
                // player closes it with Esc.
                if (!input.IsNewKeyPressed(OpenKey)) return;

                // A modified key belongs to the game, not to us: Ctrl/Alt/Shift combinations are
                // the game's own bindings, and answering them here would take them away.
                if (input.IsAnyCtrlKeyPressed() || input.IsAnyAltKeyPressed()
                    || input.IsAnyShiftKeyPressed())
                {
                    return;
                }

                IMyGui gui = MyAPIGateway.Gui;
                if (gui == null) return;

                // Typing must never open a screen: the characters go to the chat field.
                if (gui.ChatEntryVisible) return;

                // Any gameplay screen on top owns the keyboard — and this accessor answers for the
                // terminal too (it returns "MyGuiScreenTerminal"), so the two checks below overlap
                // on purpose: see the class comment.
                if (gui.ActiveGamePlayScreen != null) return;
                if (gui.GetCurrentScreen != MyTerminalPageEnum.None) return;

                // The block the player is in, if it is a cockpit. On foot this is the character and
                // the cast fails, which is the refusal the requirement asks for: there is no
                // construct to draw a schema of.
                IMyControllableEntity controlled = MyAPIGateway.Session == null
                    ? null
                    : MyAPIGateway.Session.ControlledObject;

                var cockpit = controlled as IMyCockpit;
                if (cockpit == null)
                {
                    // Debug, not Info: the player may press M on foot, and a line per press in the
                    // session log would be noise. The editor's own open line names the grid.
                    VgsLog.Debug("hotkey M pressed, but the player is not in a cockpit");
                    return;
                }

                VgsLog.Info("hotkey M: opening the editor from the cockpit");
                VgsEditorScreen.OpenFor(cockpit);
            }
            catch (Exception e)
            {
                // Throttled with its own tag: a broken poll must not silence the other throttled
                // paths, and this one runs on every tick.
                VgsLog.Throttled("hotkey", "hotkey poll failed", e, 600);
            }
        }
    }
}
