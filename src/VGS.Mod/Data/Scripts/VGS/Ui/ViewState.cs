using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// How the player is looking at the schema: the camera, the zoom and the "show hidden" mode,
    /// plus the last values written to the grid so the throttled save can skip an identical
    /// write (ADR-0022).
    ///
    /// <b>Why it is a type and not four fields on the screen.</b> "Has the view changed?" is a
    /// comparison of three values against three saved ones, and the rule that the throttle
    /// advances on the <em>attempt</em> rather than on the success is subtle enough that it has
    /// its own paragraph in ADR-0022. Both belong with the values they describe, not scattered
    /// across a five-thousand-line class.
    ///
    /// <b>What it does not own.</b> The write itself (<see cref="GridViewStore"/>), because that
    /// touches a live grid and this type must stay pure data — it is checked offline. The screen
    /// calls <see cref="TakeChanged"/> and then writes.
    ///
    /// <b>Not a clock.</b> The throttle compares frame numbers the caller passes in; this type
    /// does not read a clock of its own, because the only correct one belongs to the screen
    /// (ADR-0026).
    /// </summary>
    public sealed class ViewState
    {
        /// <summary>
        /// Position of the canvas' top-left corner in graph units. Mutated by panning, dragging
        /// the world under the pointer and by the Enter jump.
        /// </summary>
        public Vector2 Camera { get; set; }

        /// <summary>Pixels per graph unit. Clamped to <see cref="VgsConstants.MinZoom"/>..<see cref="VgsConstants.MaxZoom"/> by the caller.</summary>
        public float Zoom { get; set; }

        /// <summary>
        /// True while blocks whose "Show in VGS" flag is off are part of the schema (ADR-0023).
        /// Changing it rebuilds the graph, because whether a block is in the schema is decided
        /// while it is built.
        /// </summary>
        public bool ShowHidden { get; set; }

        // ---- what was last written (ADR-0022) ----

        /// <summary>Camera as it was when the view was last written, or recorded as written.</summary>
        private Vector2 _savedCamera;

        /// <summary>Zoom as it was when the view was last written.</summary>
        private float _savedZoom;

        private bool _savedShowHidden;

        private int _lastSaveFrame;

        /// <summary>
        /// The view as it was read from the grid when the editor opened (ADR-0022).
        ///
        /// <b>The loaded view is recorded as already saved.</b> The grid holds exactly these
        /// values — they were just read from it — so writing them back would be a no-op that
        /// costs a string format and a dictionary write. The screen used to leave the saved
        /// values "unwritten", which made the first attempt look like a change and wrote the same
        /// camera position back two seconds after opening. Nothing about that write was visible;
        /// it was only waste, and this is the one place that can rule it out.
        /// </summary>
        public ViewState(Vector2 camera, float zoom, bool showHidden)
        {
            Camera = camera;
            Zoom = zoom;
            ShowHidden = showHidden;

            _savedCamera = camera;
            _savedZoom = zoom;
            _savedShowHidden = showHidden;
        }

        /// <summary>True when the camera, the zoom or the mode differs from what was last written.</summary>
        public bool HasChanged
        {
            get
            {
                return _savedZoom != Zoom
                       || _savedShowHidden != ShowHidden
                       || _savedCamera != Camera;
            }
        }

        /// <summary>
        /// Records the current view as "already saved", so the throttled save does not write a
        /// value identical to what the grid already holds.
        /// </summary>
        public void RememberSaved(int frame)
        {
            _savedCamera = Camera;
            _savedZoom = Zoom;
            _savedShowHidden = ShowHidden;
            _lastSaveFrame = frame;
        }

        /// <summary>
        /// True when a write is due: the view changed and the throttle interval has passed since
        /// the last attempt. Advances the throttle, so a caller that gets true must write.
        ///
        /// <b>The throttle is advanced on the attempt, not on the success.</b> A failed write
        /// leaves the saved values untouched — so "changed" stays true — and if the interval were
        /// only reset by a successful write, a grid that refuses the write would make the caller
        /// format a string and try a dictionary write on every single frame for as long as the
        /// refusal lasted. Retrying at the normal cadence is enough: the value is a camera
        /// position, and the next attempt is two seconds away.
        /// </summary>
        public bool TakeChanged(int frame)
        {
            if (!HasChanged) return false;
            if (frame - _lastSaveFrame < VgsConstants.ViewSaveIntervalFrames) return false;

            _lastSaveFrame = frame;
            return true;
        }
    }
}
