using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// The shape of the toolbar a block drives other blocks with, read from the live block
    /// through the game's own interfaces (<c>VRage.Game.ModAPI.IMyToolbarProvider</c> →
    /// <c>IMyToolbar</c>). The engine types that do the same job
    /// (<c>Sandbox.Game.Screens.Helpers.IMyToolbarOwner</c> / <c>MyToolbar</c>) are prohibited
    /// to a mod script, so this layer only ever sees the ModAPI mirror.
    ///
    /// Nothing here is a table of block types: a timer reports nine slots and ten pages, a
    /// sensor reports two slots and ten pages, and the editor draws whatever it is told. A
    /// block that reports no usable toolbar simply cannot be a source, which is exactly the
    /// answer to "can this block drive other blocks".
    ///
    /// <b>Yes/no toolbars.</b> Some blocks keep two independent lists of actions — what happens
    /// on "yes" and what happens on "no" — and the editor has to see them as two groups or it
    /// writes into the wrong list. The game's own test for that is three conditions:
    /// the definition declares the background (<c>MyCubeBlockDefinition.YesNoToolbarBackground</c>,
    /// which <c>MyToolbar.Init</c> copies into <c>MyToolbar.YesNoBackground</c>), the toolbar
    /// allows it (<c>CanShowYesNoBackgrounds</c>, true for every <c>ButtonPanel</c> toolbar),
    /// and there are exactly two slots per page. The same test is what the vanilla toolbar
    /// screen makes before it draws the true/false icons.
    ///
    /// <see cref="YesNo"/> is therefore not derived from the subtype name, and not from a table:
    /// the same cockpit owns a manual toolbar (nine slots, no yes/no) and an "on lock" toolbar
    /// (two slots, yes/no), and both are described by this same type.
    /// </summary>
    public sealed class ToolbarLayout
    {
        /// <summary>Index of the toolbar this layout describes, as <c>IMyToolbarProvider</c> numbers it.</summary>
        public readonly int ToolbarIndex;

        /// <summary>Cells per page. Two for a yes/no toolbar, more for a single-group one.</summary>
        public readonly int SlotCount;

        /// <summary>Pages (the game calls them "toolbars" in the terminal UI).</summary>
        public readonly int PageCount;

        /// <summary>True when slot 0 fires on "yes" and slot 1 on "no", independently.</summary>
        public readonly bool YesNo;

        /// <summary>A block with no toolbar at all: no cells, no source ports, no "+".</summary>
        public static readonly ToolbarLayout None = new ToolbarLayout(-1, 0, 0, false);

        public ToolbarLayout(int toolbarIndex, int slotCount, int pageCount, bool yesNo)
        {
            ToolbarIndex = toolbarIndex;
            SlotCount = slotCount < 0 ? 0 : slotCount;
            PageCount = pageCount < 0 ? 0 : pageCount;
            YesNo = yesNo;
        }

        /// <summary>Total cells in this toolbar, i.e. <c>MyToolbar.ItemCount</c>.</summary>
        public int CellCount { get { return SlotCount * PageCount; } }

        /// <summary>True when the block can drive other blocks through this toolbar.</summary>
        public bool CanDrive { get { return SlotCount > 0 && PageCount > 0; } }

        /// <summary>
        /// Slot a port group writes into: 0 for <see cref="PortGroup.Yes"/>, 1 for
        /// <see cref="PortGroup.No"/>, and -1 for <see cref="PortGroup.Single"/>, meaning
        /// "any free slot of a free page".
        /// </summary>
        public int SlotFor(PortGroup group)
        {
            if (group == PortGroup.Yes) return 0;
            if (group == PortGroup.No) return 1;
            return -1;
        }

        /// <summary>Linear cell index of a (page, slot) pair, in the game's own order.</summary>
        public int CellIndex(int page, int slot)
        {
            return page * SlotCount + slot;
        }

        /// <summary>Page a linear cell index belongs to.</summary>
        public int PageOf(int cellIndex)
        {
            return SlotCount <= 0 ? 0 : cellIndex / SlotCount;
        }

        /// <summary>Slot a linear cell index occupies within its page.</summary>
        public int SlotOf(int cellIndex)
        {
            return SlotCount <= 0 ? 0 : cellIndex % SlotCount;
        }

        public override string ToString()
        {
            return "toolbar " + ToolbarIndex + ": " + SlotCount + "x" + PageCount
                   + (YesNo ? " (yes/no)" : string.Empty);
        }
    }

    /// <summary>
    /// One cell of a block's toolbar: a page and an index inside it.
    ///
    /// A connection is stored with the cell it occupies, because the toolbar *is* the
    /// connection: the editor does not keep a private wiring that the block does not have.
    /// Two connections that differ only by their cell are different connections — the game
    /// refuses the same action twice on one page and accepts it on another page.
    /// </summary>
    public struct ToolbarCell
    {
        public readonly int ToolbarIndex;
        public readonly int Index;

        /// <summary>The "no cell" value, used by edges that were never written.</summary>
        public static readonly ToolbarCell None = new ToolbarCell(-1, -1);

        public ToolbarCell(int toolbarIndex, int index)
        {
            ToolbarIndex = toolbarIndex;
            Index = index;
        }

        public bool IsValid { get { return ToolbarIndex >= 0 && Index >= 0; } }

        public bool SameAs(ToolbarCell other)
        {
            return ToolbarIndex == other.ToolbarIndex && Index == other.Index;
        }

        public override string ToString()
        {
            return "cell " + ToolbarIndex + "/" + Index;
        }
    }

}
