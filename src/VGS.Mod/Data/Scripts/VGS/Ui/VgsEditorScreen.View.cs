using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Game;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// View state of the editor: the camera, the zoom and the "show hidden" mode, and the
    /// throttled write of those three to the grid (ADR-0022).
    ///
    /// A partial of <see cref="VgsEditorScreen"/>. The values and the rule for when they are
    /// stale live in <see cref="Vgs.Ui.ViewState"/>; this file is what is left: the screen's half,
    /// which knows where the grid is and how to write to it.
    /// </summary>
    public sealed partial class VgsEditorScreen
    {
        // ------------------------------------------------------------------ view state (ADR-0022)

        /// <summary>
        /// Records the view currently on screen as "already saved", so the throttled save does
        /// not write a value identical to what the grid already holds.
        /// </summary>
        private void RememberSavedView()
        {
            _view.RememberSaved(_screenFrame);
        }

        /// <summary>
        /// Writes the view to the grid and records it as written. Returns whether the write went
        /// through; a refusal is a normal outcome (the grid may be mid-teardown).
        /// </summary>
        private bool SaveView()
        {
            if (_grid == null) return false;

            if (!GridViewStore.Save(_grid, _view.Camera, _view.Zoom, _view.ShowHidden))
            {
                return false;
            }

            RememberSavedView();
            return true;
        }

        /// <summary>
        /// Writes the view when it changed and the throttle interval has passed. Called from
        /// <see cref="OnUpdate"/> once per frame, so it must be free when nothing changed.
        ///
        /// The throttle and the "did it change" comparison are <see cref="ViewState.TakeChanged"/>
        /// — including why the throttle advances on the attempt and not on the success. This
        /// method only does the part that needs the grid.
        /// </summary>
        private void SaveViewIfChanged()
        {
            if (!_view.TakeChanged(_screenFrame)) return;

            SaveView();
        }

    }
}
