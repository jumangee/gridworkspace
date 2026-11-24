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

        /// <summary>
        /// True while blocks standing on subgrids are part of the schema (ADR-0032).
        ///
        /// <b>On by default.</b> The graph covers the whole construct, and a connection from the
        /// main grid to a subgrid block is a normal, working connection — hiding its card by
        /// default would make the schema quietly disagree with the ship. The toggle exists to
        /// take a busy subgrid out of the picture, the same way "Show in VGS" takes one block out.
        ///
        /// Changing it rebuilds the graph, for the same reason as <see cref="ShowHidden"/>:
        /// whether a node exists is decided while the graph is built, and a draw-time filter would
        /// leave edges pointing at cards that are not there.
        /// </summary>
        public bool ShowSubgrids { get; set; }

        /// <summary>
        /// True while the frame around each grid's cards is drawn (ADR-0032).
        ///
        /// <b>A drawing option, not a schema option.</b> It changes nothing about what is in the
        /// graph — only whether the border is painted — so toggling it never rebuilds anything and
        /// never touches a connection. That is also why it can be flipped freely on a big ship.
        /// </summary>
        public bool ShowGridBorders { get; set; }

        // ---- what was last written (ADR-0022) ----

        /// <summary>Camera as it was when the view was last written, or recorded as written.</summary>
        private Vector2 _savedCamera;

        /// <summary>Zoom as it was when the view was last written.</summary>
        private float _savedZoom;

        private bool _savedShowHidden;
        private bool _savedShowSubgrids;
        private bool _savedShowGridBorders;

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
            : this(camera, zoom, showHidden, true, true)
        {
        }

        /// <summary>
        /// The full form, with the two construct options (ADR-0032). The overload above passes
        /// the defaults: subgrids shown, grid borders drawn.
        /// </summary>
        public ViewState(Vector2 camera, float zoom, bool showHidden,
                         bool showSubgrids, bool showGridBorders)
        {
            Camera = camera;
            Zoom = zoom;
            ShowHidden = showHidden;
            ShowSubgrids = showSubgrids;
            ShowGridBorders = showGridBorders;

            _savedCamera = camera;
            _savedZoom = zoom;
            _savedShowHidden = showHidden;
            _savedShowSubgrids = showSubgrids;
            _savedShowGridBorders = showGridBorders;
        }

        /// <summary>True when the camera, the zoom or the mode differs from what was last written.</summary>
        public bool HasChanged
        {
            get
            {
                return _savedZoom != Zoom
                       || _savedShowHidden != ShowHidden
                       || _savedShowSubgrids != ShowSubgrids
                       || _savedShowGridBorders != ShowGridBorders
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
            _savedShowSubgrids = ShowSubgrids;
            _savedShowGridBorders = ShowGridBorders;
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
