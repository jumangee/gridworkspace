using System;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Reads and writes the editor's view state on the <b>grid</b>: where the camera was, at what
    /// zoom, and whether blocks hidden from the schema were being shown (ADR-0022).
    ///
    /// <b>Why the grid and not the cockpit.</b> The state describes how the construct is being
    /// looked at, so it must not depend on which cockpit the player happened to open the schema
    /// from: open it from a second cockpit of the same ship and the same camera has to come back.
    /// Storing it per block — the way the card positions are stored
    /// (<see cref="NodeStateStore"/>) — would give every cockpit its own camera.
    ///
    /// <b>Why not a table keyed by grid id.</b> Same reason the card positions are not keyed by
    /// block id: <c>MyObjectBuilder_CubeBlock.Remap</c> reassigns entity ids on copy, projection
    /// and grid split, and nothing remaps our keys. On the entity itself the value rides inside
    /// its <c>ComponentContainer</c> and travels with the construct.
    ///
    /// <b>Registration is part of the contract</b>, exactly as it is for the block keys: the GUID
    /// must appear in <c>Data/EntityComponents.sbc</c>, or the value survives in memory and
    /// silently disappears on reload.
    ///
    /// <b>Reads never create the component.</b> Opening the schema must not attach storage to a
    /// grid the player only looked at; only a real write does.
    ///
    /// Everything here is guarded: the grid can be gone between two frames, and an exception in
    /// the open path would take the screen down with it.
    /// </summary>
    public static class GridViewStore
    {
        /// <summary>
        /// Reads the saved view state. False means "nothing usable is stored" — a construct whose
        /// schema was never moved, or a value that failed to parse. The caller then starts from
        /// the default view rather than from a half-read one.
        /// </summary>
        public static bool TryRead(IMyCubeGrid grid, out Vector2 camera, out float zoom,
                                   out bool showHidden, out bool showSubgrids,
                                   out bool showGridBorders, out bool showConveyors)
        {
            camera = Vector2.Zero;
            zoom = 0f;
            showHidden = false;
            showSubgrids = true;
            showGridBorders = true;
            showConveyors = true;

            string raw;
            if (!TryReadValue(grid, out raw)) return false;

            return GridViewFormat.TryParse(raw, out camera, out zoom, out showHidden,
                                           out showSubgrids, out showGridBorders,
                                           out showConveyors);
        }

        /// <summary>
        /// Writes the view state to the grid. Returns false when the grid is gone or the write was
        /// refused, which is a normal outcome rather than an error: the player loses nothing but a
        /// remembered camera position.
        /// </summary>
        public static bool Save(IMyCubeGrid grid, Vector2 camera, float zoom, bool showHidden,
                                bool showSubgrids, bool showGridBorders, bool showConveyors)
        {
            if (grid == null) return false;

            try
            {
                IMyEntity entity = grid;
                MyModStorageComponentBase storage = entity.Storage;
                if (storage == null)
                {
                    // The setter is what the game itself does for CustomData: it adds the
                    // component to the container and makes get_Storage() return it. Adding the
                    // component by hand would leave the entity's own field unset.
                    storage = new MyModStorageComponent();
                    entity.Storage = storage;
                }

                storage.SetValue(VgsConstants.ViewStorageKey,
                                 GridViewFormat.Format(camera, zoom, showHidden,
                                                       showSubgrids, showGridBorders,
                                                       showConveyors));
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("gridview.save", "could not save the view state", e, 300);
                return false;
            }
        }

        private static bool TryReadValue(IMyCubeGrid grid, out string value)
        {
            value = null;
            if (grid == null) return false;

            try
            {
                MyModStorageComponentBase storage = ((IMyEntity)grid).Storage;
                if (storage == null) return false;

                return storage.TryGetValue(VgsConstants.ViewStorageKey, out value);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("gridview.read", "could not read the view state", e, 300);
                return false;
            }
        }
    }
}
