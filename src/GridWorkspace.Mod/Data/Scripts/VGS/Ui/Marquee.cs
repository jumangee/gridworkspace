using VRageMath;

namespace Vgs.Ui
{
    /// <summary>
    /// The rubber band of a marquee selection: where the left button went down, where the pointer
    /// is now, and whether that counts as a drag yet (ADR-0048).
    ///
    /// <b>Why a type and not three fields on the screen.</b> The gesture has three states that
    /// must not be confused — armed (the button is down on the void), active (the pointer has
    /// travelled far enough to draw a band) and finished — and one piece of arithmetic that is
    /// easy to get wrong and invisible in a compile: the band may be dragged in any of the four
    /// directions, so the rectangle has to be normalised before it is drawn or used to select.
    /// Both live here, where <c>tools/offline-check.ps1</c> can assert them without the game.
    ///
    /// <b>Graph units, with the click/drag decision in pixels.</b> The anchor and the pointer are
    /// stored where the selection happens — on the canvas, in graph units — so panning or zooming
    /// while the band is up would move it with the schema. The "did the hand move?" decision is
    /// <see cref="PointerGesture.ExceedsClickSlop"/>, which works in device pixels: the same
    /// allowance the right button's click-versus-pan rule uses, so "a click on the void" means one
    /// thing in this editor (ADR-0041).
    ///
    /// A value type, and a field of the screen rather than an object: the gesture exists for as
    /// long as a button is held, and a struct copy is cheaper than the null check an object would
    /// need on every frame of every drag.
    /// </summary>
    public struct Marquee
    {
        /// <summary>Where the press landed, in graph units.</summary>
        private Vector2 _anchor;

        /// <summary>Where the pointer is now, in graph units.</summary>
        private Vector2 _current;

        /// <summary>True from the press on the void until the gesture is finished or dropped.</summary>
        private bool _armed;

        /// <summary>True once the pointer has travelled far enough for the band to be drawn.</summary>
        private bool _active;

        /// <summary>
        /// True when the band adds to the existing selection instead of replacing it — a
        /// Shift+marquee, which is the marquee's counterpart of a Shift+click (ADR-0048).
        /// </summary>
        private bool _additive;

        /// <summary>True while the gesture owns the pointer.</summary>
        public bool IsArmed
        {
            get { return _armed; }
        }

        /// <summary>True when the band is worth drawing, i.e. when it selects something.</summary>
        public bool IsActive
        {
            get { return _armed && _active; }
        }

        /// <summary>True when the finished band must be added to the selection rather than replace it.</summary>
        public bool Additive
        {
            get { return _additive; }
        }

        /// <summary>Where the press landed, in graph units. Only meaningful while armed.</summary>
        public Vector2 Anchor
        {
            get { return _anchor; }
        }

        /// <summary>Where the pointer is now, in graph units. Only meaningful while armed.</summary>
        public Vector2 Current
        {
            get { return _current; }
        }

        /// <summary>
        /// Arms the gesture at the press point. <paramref name="additive"/> is the Shift state at
        /// the press, captured once: the modifier may be released mid-drag, and a band whose
        /// meaning changed halfway would select a set the player never aimed at.
        /// </summary>
        public void Begin(Vector2 graphPoint, bool additive)
        {
            _anchor = graphPoint;
            _current = graphPoint;
            _armed = true;
            _active = false;
            _additive = additive;
        }

        /// <summary>
        /// Tracks the pointer. The band becomes active as soon as the pointer has travelled
        /// farther than the click allowance from the anchor, measured in device pixels — the same
        /// threshold that separates a click from a drag everywhere else in this editor, so a
        /// marquee that selects nothing is exactly a click that travelled too little.
        ///
        /// <paramref name="zoom"/> and the client size convert the graph-space delta into pixels;
        /// the delta is multiplied by the zoom first because a graph unit is a layout unit, and
        /// <see cref="PointerGesture.ExceedsClickSlop"/> does the per-axis conversion from there.
        /// </summary>
        public void Move(Vector2 graphPoint, float zoom, float clientWidth, float clientHeight)
        {
            if (!_armed) return;

            _current = graphPoint;
            if (_active) return;

            Vector2 screenDelta = (graphPoint - _anchor) * zoom;
            if (PointerGesture.ExceedsClickSlop(screenDelta, clientWidth, clientHeight))
            {
                _active = true;
            }
        }

        /// <summary>
        /// Ends the gesture without selecting anything: the press was a click on the void, or the
        /// marquee is being dropped because the graph was rebuilt under it.
        /// </summary>
        public void Cancel()
        {
            _armed = false;
            _active = false;
            _additive = false;
            _current = _anchor;
        }

        /// <summary>
        /// The band as a rectangle with a non-negative size, whatever direction it was dragged in.
        ///
        /// The one piece of arithmetic that has to be right before the band is either drawn or
        /// used to select: dragging up and to the left produces negative extents, and a rectangle
        /// with a negative width covers nothing — the selection would silently find no nodes and
        /// the band would draw nothing, which reads as "the marquee does not work in that
        /// direction".
        /// </summary>
        public void Normalize(out Vector2 topLeft, out Vector2 size)
        {
            float left = _anchor.X < _current.X ? _anchor.X : _current.X;
            float top = _anchor.Y < _current.Y ? _anchor.Y : _current.Y;

            topLeft = new Vector2(left, top);
            size = new Vector2(
                _anchor.X < _current.X ? _current.X - _anchor.X : _anchor.X - _current.X,
                _anchor.Y < _current.Y ? _current.Y - _anchor.Y : _anchor.Y - _current.Y);
        }
    }
}
