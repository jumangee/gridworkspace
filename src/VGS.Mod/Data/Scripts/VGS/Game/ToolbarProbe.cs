using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Reads a block's toolbar shape from the live block, through the game's own interfaces.
    ///
    /// <b>Why this exists.</b> The editor's central question is "which blocks can drive other
    /// blocks". The answer is not a table of subtype names and not the
    /// <c>ShowInToolbarConfig</c> flag (that one says the block can be *driven*). It is: does
    /// the block own a toolbar whose cells the block itself activates? Space Engineers exposes
    /// exactly that as an entity component implementing
    /// <see cref="IMyToolbarProvider"/>, and the shape of each toolbar through
    /// <see cref="IMyToolbar"/>.
    ///
    /// <b>Which API, and why this one.</b> The obvious-looking path is
    /// <c>Sandbox.Game.Screens.Helpers.IMyToolbarOwner</c> plus <c>MyToolbar</c>. That path is
    /// <b>prohibited for mod scripts</b>: the game's IL whitelist allows the namespace
    /// <c>Sandbox.Game</c> exactly, and those types live in <c>Sandbox.Game.Screens.Helpers</c>,
    /// which it does not allow. A mod using them compiles locally and then fails at world load
    /// with "The type or member 'MyToolbar' is prohibited" — this mod shipped exactly that bug
    /// once. The whitelisted route is the ModAPI mirror of the same objects:
    /// <see cref="IMyToolbarProvider"/> and <see cref="IMyToolbar"/>, both in
    /// <c>VRage.Game.ModAPI</c>. The two are the same underlying objects — the engine's
    /// <c>MyToolbarProviderComponent</c> is the adapter, and the engine itself adds it to every
    /// block that drives toolbars.
    ///
    /// <b>What "the block itself activates" means.</b> A timer, a sensor, an event controller
    /// and a cockpit's "on lock" toolbar all create their toolbar with
    /// <c>MyToolbarType.ButtonPanel</c> and fire it from their own update code. A cockpit's
    /// manual toolbar is <c>Ship</c>/<c>LargeCockpit</c> and fires when the pilot presses a
    /// key; a build toolbar is <c>BuildCockpit</c>. Writing an event connection into a manual
    /// toolbar would produce a control that fires on a key press, not on the event, so those
    /// toolbars are not write targets. The toolbar type is read from the game object, never
    /// guessed from the block name.
    ///
    /// <b>Yes/no toolbars.</b> Some blocks keep two independent lists of actions — what happens
    /// on "yes" and what happens on "no". The game's own test for this is three conditions, and
    /// the editor must use the same three or it will write into the wrong list:
    ///
    ///  1. the block definition's <c>YesNoToolbarBackground</c> — <c>MyToolbar.Init</c> copies
    ///     it into <c>MyToolbar.YesNoBackground</c>, so it is the definition's own statement
    ///     that this block has two groups;
    ///  2. <c>MyToolbar.CanShowYesNoBackgrounds</c> — true for every toolbar except a cockpit's
    ///     manual and build toolbars, which <c>MyShipController.Init</c> clears. Those two are
    ///     not ButtonPanel toolbars and are already excluded by the type test above, so for
    ///     every toolbar that reaches this point the flag is true;
    ///  3. <c>SlotCount == 2</c> — the two groups are slot 0 and slot 1 of each page.
    ///
    /// That is the test <c>MyGuiControlToolbar.SetGridItemAt</c> makes before it draws the
    /// "true"/"false" icons, and the one <c>MySensorBlock.OnFirstEnter</c> / <c>OnLastLeave</c>
    /// relies on when it activates slot 0 and slot 1. A two-slot toolbar without the definition
    /// flag is therefore a normal two-slot toolbar: everything in it fires together, and the
    /// editor must not split it. See <c>docs/01-research/api-toolbar.md</c>.
    ///
    /// Nothing here throws: this runs inside the refresh pass, and a block can be destroyed
    /// between two frames.
    /// </summary>
    public static class ToolbarProbe
    {
        /// <summary>
        /// Describes the toolbar a block drives other blocks with, or
        /// <see cref="ToolbarLayout.None"/> when it has none the block fires itself.
        /// </summary>
        public static ToolbarLayout Probe(IMyTerminalBlock block)
        {
            if (block == null) return ToolbarLayout.None;

            try
            {
                IMyToolbarProvider provider = FindProvider(block);
                if (provider == null) return ToolbarLayout.None;

                int count = provider.ToolbarCount;
                for (int i = 0; i < count; i++)
                {
                    IMyToolbar toolbar = provider.GetToolbar(i);
                    if (toolbar == null) continue;

                    // Only a toolbar the block activates from its own logic is a write
                    // target: see the type comment for why the manual ones are excluded.
                    if (toolbar.Type != MyToolbarType.ButtonPanel) continue;

                    int slots = toolbar.SlotCount;
                    int pages = toolbar.PageCount;
                    if (slots <= 0 || pages <= 0) continue;

                    // Conditions 2 and 3 of the game's test. Condition 2 holds for every
                    // ButtonPanel toolbar (only a cockpit's Ship/BuildCockpit toolbars clear
                    // it, and those were rejected above); condition 3 is the slot count.
                    bool yesNo = slots == 2 && DefinitionSaysYesNo(block);

                    return new ToolbarLayout(i, slots, pages, yesNo);
                }
            }
            catch (Exception e)
            {
                // A block whose toolbar cannot be read simply has no toolbar as far as the
                // editor is concerned; it stays on the canvas as a target.
                VgsLog.Throttled("toolbar-probe", "could not read toolbar layout", e, 300);
            }

            return ToolbarLayout.None;
        }

        /// <summary>
        /// The block's toolbar at this index, or null. Public so that
        /// <see cref="ToolbarWriter"/> resolves the toolbar the same way the probe did — one
        /// implementation of "where is the toolbar", not two.
        /// </summary>
        public static IMyToolbar GetToolbar(IMyTerminalBlock block, int toolbarIndex)
        {
            if (block == null || toolbarIndex < 0) return null;

            IMyToolbarProvider provider = FindProvider(block);
            if (provider == null) return null;
            if (toolbarIndex >= provider.ToolbarCount) return null;

            return provider.GetToolbar(toolbarIndex);
        }

        /// <summary>
        /// The entity component that answers toolbar questions for this block, or null.
        ///
        /// It is looked up by <b>interface</b>, not by the concrete engine type: the engine
        /// registers it as <c>MyToolbarProviderComponent</c>, but a component that answers
        /// <see cref="IMyToolbarProvider"/> is all the editor needs, and looking for the
        /// interface keeps working if the engine renames its class. A block with no such
        /// component has no toolbar of its own and cannot drive anything.
        /// </summary>
        private static IMyToolbarProvider FindProvider(IMyTerminalBlock block)
        {
            // Components live on the entity. The ModAPI block interface does not expose the
            // container, so the lookup goes through the entity type that owns it — every cube
            // block in the world is a MyEntity.
            var owner = block as VRage.Game.Entity.MyEntity;
            if (owner == null) return null;

            VRage.Game.Components.Interfaces.IMyEntityComponentContainer components = owner.Components;
            if (components == null) return null;

            // Enumerating rather than Get<T>() is deliberate: Get<T> looks the component up by
            // the exact type argument, which would mean naming the engine's class here.
            foreach (MyComponentBase component in components)
            {
                var provider = component as IMyToolbarProvider;
                if (provider != null) return provider;
            }

            return null;
        }

        /// <summary>
        /// True when the block's definition declares the yes/no toolbar background.
        ///
        /// This is condition 1 of the game's three-part test, and the only one that is not
        /// visible through the ModAPI toolbar interface: <c>IMyToolbar</c> exposes the type,
        /// slot count and page count, but not the background flag. Reading it from the
        /// definition is not a guess — <c>MyToolbar.Init</c> copies exactly this field into
        /// <c>MyToolbar.YesNoBackground</c>, so the definition is the source, not a proxy for
        /// it.
        /// </summary>
        private static bool DefinitionSaysYesNo(IMyTerminalBlock block)
        {
            var cubeBlock = block as VRage.Game.ModAPI.IMyCubeBlock;
            if (cubeBlock == null) return false;

            IMySlimBlock slim = cubeBlock.SlimBlock;
            if (slim == null) return false;

            var definition = slim.BlockDefinition as MyCubeBlockDefinition;
            if (definition == null) return false;

            return definition.YesNoToolbarBackground;
        }
    }
}
