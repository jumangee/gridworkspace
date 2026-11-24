using System;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;
using Vgs.Core;

namespace Vgs.Ui
{
    /// <summary>
    /// The editor's icons, resolved once per session.
    ///
    /// Three things live here and nowhere else, because each of them is a decision the rest of
    /// the editor should not have to repeat (ADR-0019):
    ///
    ///  * <b>Whether icons are usable at all.</b> The textures ship with the mod and are
    ///    registered in `Data\GuiTextures.sbc`; whether the game picked that registration up is
    ///    only knowable at run time. When it did not, the editor draws its old geometry instead —
    ///    an icon is decoration, and its absence must never take a control off the screen.
    ///  * <b>The material ids.</b> `MyStringId.GetOrCompute` hashes a string, so it is resolved
    ///    once here rather than in the draw path, which runs for every port of every visible card
    ///    on every frame.
    ///
    /// There is deliberately no size policy here any more. A per-icon legibility floor used to
    /// live in this class; it was removed after acceptance in the game, because falling back to
    /// the old shape at small zoom made the control change shape as the player zoomed out — a
    /// ring that becomes a disc, a square-with-plus that becomes a circled "+". ADR-0019 §6
    /// records the reversal and the simulation that justified it.
    ///
    /// Nothing here allocates after <see cref="Ensure"/>: the arrays are sized once and indexed by
    /// <see cref="VgsIcon"/>.
    /// </summary>
    public static class VgsIcons
    {
        private static readonly MyStringId[] Materials = new MyStringId[VgsIconAtlas.Count];
        private static readonly bool[] Available = new bool[VgsIconAtlas.Count];

        private static bool _resolved;

        /// <summary>
        /// Attempt at which an unresolved icon set may be tried again. Negative means "try now".
        ///
        /// Retrying exists because two things can lag behind the first draw: the GUI-texture
        /// table, which is rebuilt when a world loads, and the textures themselves, which the
        /// renderer streams in. A one-shot resolution would disable icons for the whole session
        /// over a few frames of bad timing. The retry is throttled because each attempt hashes
        /// strings and walks a dictionary.
        ///
        /// <b>Counted in attempts, not in session frames.</b> `VgsSession.Frame` only advances from
        /// `UpdateAfterSimulation`, which a paused session skips — and the editor pauses the game.
        /// A frame-based throttle therefore never expired while the editor was open, so an icon set
        /// that failed to resolve at open stayed failed for the whole screen (ADR-0025 §Consequences
        /// records the general form of this bug). `Ensure` is called once per draw, so counting
        /// calls gives the same spacing whenever frames do advance, and keeps working when they do
        /// not.
        /// </summary>
        private static int _retryAfterAttempt = -1;

        /// <summary>How many attempts have been made, used as the retry clock.</summary>
        private static int _attempt;

        /// <summary>How many attempts to wait before trying an unresolved icon set again.</summary>
        private const int RetryIntervalAttempts = 300;

        /// <summary>
        /// How many icons resolved the last time a warning was logged, or -1 before the first
        /// warning.
        ///
        /// Retrying a permanently broken set would otherwise write a warning every 300 frames for
        /// the whole session, which is log spam that buries the one line that matters. The warning
        /// is emitted when the answer changes, and once more if it never improves — which is all a
        /// reader needs: "it is still 2 of 3" is not news after the first time.
        /// </summary>
        private static int _lastWarnedResolved = -1;

        /// <summary>
        /// Resolves every icon's material. Called from the per-frame screen-space sync, so it
        /// runs often; the <see cref="_resolved"/> short-circuit makes a resolved set free.
        ///
        /// A missing texture is a warning, not an error: the editor is fully usable without
        /// icons.
        /// </summary>
        public static void Ensure()
        {
            if (_resolved) return;

            if (_retryAfterAttempt >= 0 && _attempt < _retryAfterAttempt) return;

            // Saturating, because a session can run for a very long time and this only has to
            // answer "has the interval elapsed". Wrapping into negative would silently disable the
            // retry instead of merely mistiming it.
            if (_attempt < int.MaxValue) _attempt++;

            int resolved = 0;
            for (int i = 0; i < VgsIconAtlas.Count; i++)
            {
                var icon = (VgsIcon)i;
                string name = VgsIconAtlas.MaterialName(icon);
                if (string.IsNullOrEmpty(name)) continue;

                try
                {
                    MyStringId id = MyStringId.GetOrCompute(name);

                    // Two separate questions, and both have to be answered.
                    //
                    // IsMaterialAvailable asks whether this name resolves to a texture path at
                    // all — that is the GuiTextures.sbc registration. GetTextureSize then asks
                    // whether that path points at something the renderer can read: it goes
                    // through ContentIndex.TryGetImageSize, which is a disk index of image
                    // headers, not a VRAM query.
                    //
                    // So this proves the texture is *findable and readable*, not that it is
                    // resident. The stronger question — MyRenderProxy.IsTextureLoaded — is not on
                    // the mod whitelist. The residual risk is narrow and accepted: on the first
                    // frame or two after a world load the sprite can still be dropped and the
                    // control draws nothing rather than its old shape. Every later frame is
                    // covered, and the texture is small enough that the window is a frame or two.
                    if (!MyScreenSpaceSprite.IsMaterialAvailable(id)) continue;
                    if (MyScreenSpaceSprite.GetTextureSize(id).X <= 0f) continue;

                    Materials[i] = id;
                    Available[i] = true;
                    resolved++;
                }
                catch (Exception e)
                {
                    VgsLog.Error("could not resolve icon material '" + name + "'", e);
                }
            }

            if (resolved == VgsIconAtlas.Count)
            {
                _resolved = true;
                _retryAfterAttempt = -1;
                _lastWarnedResolved = -1;
                VgsLog.Info("icons ready (" + resolved + " texture(s), prefix '" +
                            VgsIconAtlas.MaterialPrefix + "')");
                return;
            }

            // Not all of them: keep whatever resolved, retry later, and say so once per change.
            //
            // Clamped rather than added: at the saturating end of the counter the addition would
            // wrap negative, the `>= 0` guard above would stop returning, and Ensure would retry
            // on every call — hashing strings per draw. Unreachable in practice, but the increment
            // three lines above argues about exactly this wrap.
            _retryAfterAttempt = _attempt > int.MaxValue - RetryIntervalAttempts
                                     ? int.MaxValue
                                     : _attempt + RetryIntervalAttempts;
            if (resolved == _lastWarnedResolved) return;
            _lastWarnedResolved = resolved;

            if (resolved > 0)
            {
                // Draw the ones that do resolve, and keep retrying for the rest — latching here
                // would leave a single late texture stuck on plain shapes for the whole session,
                // which is exactly the case the retry exists for. Naming the count matters too:
                // one mistyped SubtypeName would otherwise show up only as one control quietly
                // using geometry.
                VgsLog.Warn("icons partially ready (" + resolved + " of " + VgsIconAtlas.Count +
                            " textures). Missing ones draw plain shapes; check Data\\GuiTextures.sbc.");
                return;
            }

            VgsLog.Warn("icon textures unavailable: controls fall back to plain shapes. " +
                        "Check Data\\GuiTextures.sbc and Textures\\VGS\\Icons\\*.png.");
        }

        /// <summary>
        /// The material for <paramref name="icon"/>, or false when this icon has no texture. A
        /// caller must check: drawing with an empty material silently draws nothing.
        /// </summary>
        public static bool TryGetMaterial(VgsIcon icon, out MyStringId material)
        {
            material = MyStringId.NullOrEmpty;

            int index = (int)icon;
            if (index < 0 || index >= Materials.Length) return false;
            if (!Available[index]) return false;

            material = Materials[index];
            return true;
        }

        /// <summary>Forgets the resolved materials. Called when the session unloads.</summary>
        public static void Release()
        {
            for (int i = 0; i < Materials.Length; i++)
            {
                Materials[i] = MyStringId.NullOrEmpty;
                Available[i] = false;
            }

            _resolved = false;
            _retryAfterAttempt = -1;
            _attempt = 0;
            _lastWarnedResolved = -1;
        }
    }
}
