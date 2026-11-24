using VRageMath;

namespace Vgs.Ui
{
    /// <summary>
    /// Every colour the editor paints with, in one place (ARGB via <see cref="Color"/>).
    ///
    /// <b>Why a separate type.</b> These were 32 <c>private static readonly</c> fields at the top
    /// of <c>VgsEditorScreen</c>, which put the thing most likely to be tuned — the palette — in
    /// the middle of the thing least likely to be touched, the screen's lifecycle. A colour is
    /// not screen state: it is a constant shared by every drawing method, and it belongs with the
    /// other constants, not with the fields.
    ///
    /// <b>Why the call sites still say <c>ColNode</c>.</b> The two drawing files import this type
    /// with <c>using static</c>. Qualifying all 112 references would put
    /// <c>VgsPalette.ColNodeBorder</c> inside expressions like
    /// <c>_searchField.Hot ? ColNodeHover : ColNodeBorder</c>, where the extra word carries no
    /// information — there is exactly one palette, and the import is at the top of the file. The
    /// gain the extraction was for (one home for the colours, the screen file smaller) does not
    /// depend on how the call sites read.
    ///
    /// <b>Not in <c>VgsConstants</c>.</b> That type is deliberately free of game references so it
    /// can be compiled into the offline gate; <see cref="Color"/> is <c>VRageMath</c>, which the
    /// model may use, but the palette is a *drawing* concern and has no business in a file about
    /// geometry and timings (ADR-0003).
    /// </summary>
    public static class VgsPalette
    {
        public static readonly Color ColBackdrop = new Color(12, 15, 20, 235);
        public static readonly Color ColCanvas = new Color(18, 22, 29, 255);
        public static readonly Color ColGridMinor = new Color(30, 36, 46, 255);
        public static readonly Color ColGridMajor = new Color(44, 52, 66, 255);
        public static readonly Color ColNode = new Color(38, 45, 58, 255);
        public static readonly Color ColNodeBorder = new Color(70, 84, 105, 255);
        public static readonly Color ColNodeSelected = new Color(255, 170, 60, 255);
        public static readonly Color ColNodeHover = new Color(110, 190, 255, 255);
        public static readonly Color ColText = new Color(226, 232, 240, 255);
        public static readonly Color ColTextDim = new Color(140, 152, 170, 255);

        /// <summary>
        /// The damage band on a card: a dark red at half opacity, so it reads as "this block is
        /// hurt" without competing with the label, the buttons and the port colours that sit on
        /// top of it (ADR-0028).
        ///
        /// Half opacity because the card's own fill is opaque and the band is composited over it:
        /// (200,60,55) at 128 lands near (119,52,56) over the card's (38,45,58) — clearly red,
        /// clearly darker than the text, and still a card rather than a warning box. A fully
        /// opaque red would turn a damaged block into a different kind of object, and the player
        /// would lose the card's own identity in a fight.
        /// </summary>
        public static readonly Color ColDamage = new Color(200, 60, 55, 128);

        public static readonly Color ColPortIn = new Color(110, 190, 255, 255);
        public static readonly Color ColPortInFill = new Color(28, 44, 62, 255);
        public static readonly Color ColPortOut = new Color(120, 220, 160, 255);
        public static readonly Color ColPortOutFill = new Color(26, 48, 38, 255);
        public static readonly Color ColLine = new Color(120, 220, 160, 190);
        public static readonly Color ColLinePending = new Color(255, 170, 60, 235);

        /// <summary>
        /// A watch line is a third kind of line: the controller <em>observes</em> a block instead
        /// of calling it (ADR-0017). It is drawn in a warm yellow, dashed, so it cannot be
        /// confused with a connection at a glance — the fill is the same family as the watch
        /// bar's, which is what ties the line to the port it leaves from.
        /// </summary>
        public static readonly Color ColWatch = new Color(240, 200, 90, 255);
        public static readonly Color ColWatchFill = new Color(62, 52, 24, 255);
        public static readonly Color ColWatchLine = new Color(240, 200, 90, 210);

        /// <summary>
        /// Colour of a refused watch line: the same line the player is dragging, turned red for
        /// about a second (ADR-0017 §5). Red because the failure is not "you missed", it is "this
        /// block cannot be attached" — and the line itself is the only thing under the cursor.
        /// </summary>
        public static readonly Color ColWatchError = new Color(255, 90, 90, 245);

        /// <summary>
        /// A group is a light frame <em>behind</em> its members' cards (ADR-0015). Light, because
        /// it has to be readable on the dark canvas without competing with the cards it contains:
        /// a card is what the player acts on, the frame only says "these belong together".
        ///
        /// The fill is semi-transparent, so what the player sees is the fill composited over the
        /// canvas: (150,158,170) at alpha 110 over (18,22,29) lands near (75,80,88), which is
        /// clearly lighter than a card (38,45,58) — the first value tried here, (96,104,118) at
        /// alpha 70, composited to (39,45,53) and was indistinguishable from the card.
        /// The title strip is deliberately <em>darker</em> than the fill: it carries the group's
        /// name in the editor's light text colour, and light text on the light fill would not read.
        /// </summary>
        public static readonly Color ColGroupFill = new Color(150, 158, 170, 110);
        public static readonly Color ColGroupBorder = new Color(196, 206, 220, 220);
        public static readonly Color ColGroupTitle = new Color(90, 98, 112, 240);
        public static readonly Color ColGroupSelected = new Color(255, 170, 60, 255);

        /// <summary>
        /// A card's buttons are bare icons in the light "control" colour, warming to
        /// <see cref="ColPlusFillHot"/> under the pointer. Light, because a control that starts a
        /// modal gesture has to be the brightest thing on the canvas; bare, because an icon
        /// carries its own shape and a disc behind it would be a second, louder shape (ADR-0020,
        /// and ADR-0019 §5 for the same reversal on the port's "x").
        /// </summary>
        public static readonly Color ColPlusFill = new Color(244, 247, 252, 255);
        public static readonly Color ColPlusFillHot = new Color(255, 214, 140, 255);

        public static readonly Color ColMenu = new Color(26, 32, 42, 250);
        public static readonly Color ColMenuBorder = new Color(90, 106, 130, 255);
        public static readonly Color ColMenuRowHot = new Color(52, 66, 88, 255);
        public static readonly Color ColBottomBar = new Color(24, 29, 38, 255);

        /// <summary>
        /// Colour of a failure message: warm enough to read as a problem, dim enough not to shout
        /// from a bar the user looks at only when something goes wrong.
        /// </summary>
        public static readonly Color ColWarning = new Color(255, 176, 120, 255);
    }
}
