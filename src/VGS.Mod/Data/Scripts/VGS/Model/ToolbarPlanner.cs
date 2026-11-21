using System;

namespace Vgs.Model
{
    /// <summary>Why a connection could not be placed in a toolbar.</summary>
    public enum ToolbarPlacement
    {
        /// <summary>A cell was found and <c>index</c> holds it.</summary>
        Ok = 0,

        /// <summary>Every page has no free cell of the required slot.</summary>
        NoFreeCell = 1,

        /// <summary>Every page that has a free cell already holds this very action.</summary>
        ActionOnEveryPage = 2,

        /// <summary>The layout has no cells at all, or the group does not fit it.</summary>
        NotDrivable = 3
    }

    /// <summary>
    /// Decides which cell of a block's toolbar a new connection goes into.
    ///
    /// <b>Why this is a separate, pure type.</b> The rules below are the whole of the project's
    /// toolbar behaviour, they are the thing a user notices when they are wrong ("the tenth
    /// lamp did nothing"), and they are decidable from a layout plus two questions about the
    /// current contents. Keeping them here means they are exercised by
    /// <c>tools/offline-check.ps1</c> on every build instead of being discovered in game.
    ///
    /// <b>The rules, in the order they matter.</b>
    ///
    ///  1. A yes/no toolbar has one slot per fire group: "yes" writes slot 0 of a page, "no"
    ///     writes slot 1. A single-group toolbar takes any free cell. This is not a
    ///     preference — the game fires slot 0 and slot 1 independently, so an item in the
    ///     wrong slot would fire on the wrong event.
    ///  2. The same action on the same target block may appear only once per <i>page</i>. The
    ///     vanilla toolbar-config screen deletes every other slot of the current page holding
    ///     an equal item before it drops a new one
    ///     (<c>MyGuiScreenToolbarConfigBase.DropGridItemToToolbar</c>), so a duplicate written
    ///     on one page would silently vanish the first time the player edited that page. A
    ///     repeat therefore goes to another page.
    ///  3. When every page already holds that action, the connection cannot exist at all, and
    ///     the caller has to say so. That is the "tenth repeating action" case: a timer has
    ///     ten pages, so the eleventh identical connection is impossible, not merely awkward.
    ///  4. Pages are tried in order and the first usable cell wins, so the result is stable:
    ///     the same toolbar state always produces the same cell, which is what keeps the
    ///     picture reproducible across refreshes.
    /// </summary>
    public static class ToolbarPlanner
    {
        /// <summary>
        /// Picks a cell for a connection of <paramref name="group"/>.
        ///
        /// <paramref name="isCellEmpty"/> and <paramref name="pageHoldsSameAction"/> are
        /// supplied by the caller because only it can read the live toolbar; the decision
        /// itself needs nothing else. Neither callback may throw — the caller wraps them.
        /// </summary>
        public static ToolbarPlacement Plan(ToolbarLayout layout, PortGroup group,
                                            Func<int, bool> isCellEmpty,
                                            Func<int, bool> pageHoldsSameAction,
                                            out int index)
        {
            index = -1;

            if (layout == null || !layout.CanDrive) return ToolbarPlacement.NotDrivable;
            if (isCellEmpty == null || pageHoldsSameAction == null) return ToolbarPlacement.NotDrivable;

            int slot = layout.SlotFor(group);
            if (slot >= layout.SlotCount) return ToolbarPlacement.NotDrivable;

            bool sawDuplicate = false;

            for (int page = 0; page < layout.PageCount; page++)
            {
                int candidate = -1;

                if (slot >= 0)
                {
                    int direct = layout.CellIndex(page, slot);
                    if (isCellEmpty(direct)) candidate = direct;
                }
                else
                {
                    for (int s = 0; s < layout.SlotCount; s++)
                    {
                        int probe = layout.CellIndex(page, s);
                        if (isCellEmpty(probe)) { candidate = probe; break; }
                    }
                }

                if (candidate < 0) continue;

                if (pageHoldsSameAction(page))
                {
                    sawDuplicate = true;
                    continue;
                }

                index = candidate;
                return ToolbarPlacement.Ok;
            }

            return sawDuplicate ? ToolbarPlacement.ActionOnEveryPage : ToolbarPlacement.NoFreeCell;
        }

        /// <summary>
        /// Which fire group a cell of this layout belongs to. A single-group toolbar has one
        /// group; a yes/no one maps slot 0 to <see cref="PortGroup.Yes"/> and every other slot
        /// to <see cref="PortGroup.No"/>.
        ///
        /// Lives here rather than in the game layer because the graph needs it when it reads a
        /// block's toolbar back: an item found in slot 1 of a yes/no toolbar is a connection of
        /// the "no" port, not of the "yes" one.
        /// </summary>
        public static PortGroup GroupOfCell(ToolbarLayout layout, int cellIndex)
        {
            if (layout == null || !layout.YesNo) return PortGroup.Single;
            return layout.SlotOf(cellIndex) == 0 ? PortGroup.Yes : PortGroup.No;
        }
    }
}
