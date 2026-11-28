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

        /// <summary>
        /// The field's grid: two kinds of line that differ in <b>colour only</b>, never in
        /// thickness (ADR-0032 §3). Both are one device pixel thick at every zoom, so these two
        /// values are the field's entire look — raise them to make the field louder, lower them
        /// to make it quieter. Nothing else in the editor reads them.
        ///
        /// Minor line is ColGridMinor, every fifth (ColGridMajor) line is the lighter.
        /// </summary>
        public static readonly Color ColGridMinor = new Color(30, 30, 30, 255);
        public static readonly Color ColGridMajor = new Color(50, 50, 50, 255);
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

        /// <summary>
        /// The marquee band: the one-pixel dashed outline the player drags on the void to select
        /// several cards at once (ADR-0048).
        ///
        /// <b>The selection colour, not a new one.</b> A selected card's border is
        /// <see cref="ColNodeSelected"/>, and the band is the same promise one step earlier — "this
        /// is what is about to be selected". Giving it its own hue would add a fourth meaning to a
        /// palette whose colours already carry the watch yellow, the connection green and the
        /// damage red. It is slightly translucent so it reads as an annotation drawn over the
        /// schema rather than as another frame belonging to it.
        /// </summary>
        public static readonly Color ColMarquee = new Color(255, 170, 60, 200);

        public static readonly Color ColPortIn = new Color(110, 190, 255, 255);
        public static readonly Color ColPortInFill = new Color(28, 44, 62, 255);
        public static readonly Color ColPortOut = new Color(120, 220, 160, 255);
        public static readonly Color ColPortOutFill = new Color(26, 48, 38, 255);
        public static readonly Color ColLine = new Color(120, 220, 160, 190);
        public static readonly Color ColLinePending = new Color(255, 170, 60, 235);

        /// <summary>
        /// A conveyor pipe: two lines of <c>#444455</c> with a bore between them (ADR-0040).
        ///
        /// <b>Deliberately the quietest thing on the canvas.</b> A pipe is a fact about the
        /// ship, not a gesture and not a signal: it must be readable as "these blocks are
        /// plumbed together" without competing with a connection line (green), a watch line
        /// (amber) or a card. The value is the one the requirement names, kept verbatim rather
        /// than tuned, so the acceptance check in the game is a comparison against a stated
        /// colour. On the canvas (18,22,29) it reads as a cool dark grey-blue — visible when
        /// looked for, invisible when the player is reading the logic.
        /// </summary>
        public static readonly Color ColConveyorPipe = new Color(68, 68, 85, 255);

        /// <summary>
        /// The highlight of a line: white, drawn <b>under</b> the line it belongs to (ADR-0049).
        ///
        /// <b>White, and not a brighter version of the line's colour.</b> The highlight has to say
        /// "this one" without saying what kind of line it is: green means "this fires that" and
        /// amber means "this watches that" (ADR-0017), and a highlight in either colour would
        /// claim the line is the other kind. White is the one value in this palette that carries
        /// no meaning of its own.
        ///
        /// It is drawn first, at the same two-pixel weight and on the same path, and the line is
        /// drawn over it: what the player sees is the line's own rhythm with white in its gaps.
        /// The constant is opaque — the caller fades it by the line's own alpha, so a line dimmed
        /// by the search (ADR-0023) is highlighted as faintly as it is drawn.
        /// </summary>
        public static readonly Color ColLineHighlight = new Color(255, 255, 255, 255);

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

        /// <summary>
        /// The card's block-power switch, in its three states (ADR-0035).
        ///
        /// <b>These are the only colours on the canvas that mean a state of the block itself.</b>
        /// Every other control is near-white at rest and warms under the pointer, because its
        /// colour says what the pointer is doing; the switch is different by requirement — its
        /// colour *is* the reading, and it is the reading the player came to the schema for.
        ///
        /// Green for on: the same family as <see cref="ColPortOut"/> and <see cref="ColLine"/>,
        /// which already mean "this is live" in this palette, so the picture has one green rather
        /// than two.
        ///
        /// Grey <c>#777777</c> for off and dark red <c>#770000</c> for "too damaged to switch on":
        /// both are the values the requirement names, kept verbatim rather than tuned, so the
        /// acceptance check in the game is a comparison against a stated number. The red is dark
        /// on purpose — it must not shout louder than the damage band it accompanies — and it
        /// composites to about the same brightness as that band, which is the look the band was
        /// accepted with (ADR-0028 §3).
        /// </summary>
        public static readonly Color ColPowerOn = new Color(110, 215, 130, 255);
        public static readonly Color ColPowerOff = new Color(119, 119, 119, 255);
        public static readonly Color ColPowerUnavailable = new Color(119, 0, 0, 255);

        /// <summary>
        /// The base colour of every element of a card's <b>state widget</b> (ADR-0042): the frame and
        /// the fill of a bar, the face ring and both hands of a clock.
        ///
        /// <b>One grey for all of them, because the shape already carries the reading.</b> A bar's
        /// height says how full a container is; colouring it by value would be a second scale for the
        /// same number, and would collide with the damage band and the power switch, which are the
        /// only things on a card whose colour is a state. The value is the requirement's own
        /// <c>#777777</c>.
        ///
        /// It happens to equal <see cref="ColPowerOff"/>. The two are separate constants on purpose:
        /// one is a reading and the other is "this block is switched off", and a future tuning of
        /// either must not silently repaint the other.
        /// </summary>
        public static readonly Color ColWidget = new Color(119, 119, 119, 255);

        /// <summary>
        /// The charge-mode bolt of a battery widget while the battery is <b>recharging</b>
        /// (ADR-0042 §4).
        ///
        /// <b>The palette's existing yellow.</b> It is the same value as <see cref="ColWatch"/>, and
        /// deliberately so: the requirement asked for the schema to have one yellow and one red, not
        /// a third pair. The constant is separate because this one means "the charge is rising",
        /// while the watch colour means "this line is an observation" — two meanings that happen to
        /// share a hue, and a change to one should not silently repaint the other.
        /// </summary>
        public static readonly Color ColBatteryRecharge = new Color(240, 200, 90, 255);

        /// <summary>
        /// The charge-mode bolt while the battery is <b>discharging</b> (ADR-0042 §4). The palette's
        /// existing red — the same value as <see cref="ColWatchError"/> — for the same reason
        /// <see cref="ColBatteryRecharge"/> is the existing yellow.
        ///
        /// A brighter red than <see cref="ColPowerUnavailable"/>: that one means "you cannot switch
        /// this on", which is a dead end, while this one is an active, working block that is using up
        /// its charge. The two must not read as the same complaint.
        /// </summary>
        public static readonly Color ColBatteryDischarge = new Color(255, 90, 90, 255);

        /// <summary>
        /// Lightens a colour towards white, keeping its alpha, and returns it.
        ///
        /// It exists for the one control whose colour carries a state rather than an affordance:
        /// the power switch cannot warm to <see cref="ColPlusFillHot"/> under the pointer without
        /// losing the reading the player is about to act on, so hover brightens the state colour
        /// instead of replacing it. A struct in, a struct out — nothing is allocated, which is what
        /// lets it be called from the draw path.
        /// </summary>
        public static Color Brighten(Color color, float amount)
        {
            if (amount <= 0f) return color;
            if (amount > 1f) amount = 1f;

            int r = color.R + (int)((255 - color.R) * amount);
            int g = color.G + (int)((255 - color.G) * amount);
            int b = color.B + (int)((255 - color.B) * amount);

            if (r > 255) r = 255;
            if (g > 255) g = 255;
            if (b > 255) b = 255;

            return new Color(r, g, b, color.A);
        }

        public static readonly Color ColMenu = new Color(26, 32, 42, 250);
        public static readonly Color ColMenuBorder = new Color(90, 106, 130, 255);
        public static readonly Color ColMenuRowHot = new Color(52, 66, 88, 255);
        public static readonly Color ColBottomBar = new Color(24, 29, 38, 255);

        /// <summary>
        /// The line along the bottom bar's top edge.
        ///
        /// <b>Deliberately not a grid colour.</b> It used to borrow
        /// <see cref="ColGridMajor"/>, which made every tuning of the field's grid repaint the
        /// chrome as well: the two are different objects — the field's grid is tuned against the
        /// canvas, the bar's edge against the bar — and one value cannot serve both.
        /// </summary>
        public static readonly Color ColBarSeparator = new Color(44, 52, 66, 255);

        /// <summary>
        /// Colour of a failure message: warm enough to read as a problem, dim enough not to shout
        /// from a bar the user looks at only when something goes wrong.
        /// </summary>
        public static readonly Color ColWarning = new Color(255, 176, 120, 255);

        /// <summary>
        /// Border of the grid the editor was opened on (ADR-0032): the quieter of the two, because
        /// the player is looking at the construct from that ship part and a border at full strength
        /// around everything would read as the outer edge of the picture rather than as one grid
        /// among several.
        ///
        /// A cool grey, deliberately unlike the group's warm light grey: the two borders nest, and
        /// telling them apart by temperature is faster than telling them apart by position.
        /// </summary>
        public static readonly Color ColGridFrameMain = new Color(96, 120, 148, 150);

        /// <summary>
        /// Border of a subgrid (ADR-0032): slightly brighter than the main grid's, because a
        /// subgrid is the thing the player came to look at — it is the part that was missing from
        /// the schema before this feature existed.
        /// </summary>
        public static readonly Color ColGridFrameSub = new Color(126, 158, 194, 185);

        /// <summary>
        /// Fill of a text field's box, in the argument dialog (ADR-0050): darker than the panel
        /// it sits on, so the box reads as a place to type rather than as a label. The same value
        /// the canvas uses, because a field and the canvas are both "behind the interface".
        /// </summary>
        public static readonly Color ColField = new Color(18, 22, 29, 255);

        /// <summary>
        /// Fill of the dialog's primary button — the one that confirms. The accent orange is the
        /// same colour the editor uses for "this is the thing you are acting on" (selection,
        /// marquee, pending line), which is what makes the right answer obvious at a glance.
        /// </summary>
        public static readonly Color ColButtonPrimary = new Color(184, 116, 30, 255);

        /// <summary>The primary button under the pointer: the same hue, one step brighter.</summary>
        public static readonly Color ColButtonPrimaryHot = new Color(224, 148, 46, 255);

        /// <summary>
        /// Text on the primary button. Near-black rather than white: the fill is bright, and a
        /// white caption on it would be the lowest-contrast pair in the whole editor.
        /// </summary>
        public static readonly Color ColButtonPrimaryText = new Color(24, 18, 10, 255);

        /// <summary>
        /// Colour of a refusal inside the argument dialog — the wrong field's frame and the line
        /// under the fields (ADR-0050). The same red the watch refusal uses, because both mean
        /// "the game will not take this", and one meaning gets one colour.
        /// </summary>
        public static readonly Color ColWarn = new Color(255, 90, 90, 245);
    }
}
