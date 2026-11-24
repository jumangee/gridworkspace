using System;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using Vgs.Core;

namespace Vgs.Game
{
    /// <summary>
    /// Reads the language the player chose in the game's own options and hands it to
    /// <see cref="VgsText"/> (ADR-0033).
    ///
    /// <b>The mod has no language setting of its own, on purpose.</b> A player who set the game to
    /// Russian has already answered the question, and a second switch in a mod screen would mean
    /// two places to look when the text is wrong. The value comes from
    /// <c>MyAPIGateway.Session.Config.Language</c> — the same field the game's options screen
    /// writes, verified against the installed assemblies (see
    /// <c>docs/01-research/api-language.md</c>).
    ///
    /// <b>This is the only place that knows the game's enum.</b> Everything below it speaks
    /// <see cref="VgsLanguage"/>, so a third language is a table plus one case here; the mapping
    /// is the whole translation layer, and it is deliberately small enough to read in one look.
    ///
    /// <b>Per client, not per world.</b> The config is a local file, so in multiplayer every
    /// player reads their own machine's language and sees the editor in it. Nothing about the
    /// language travels over the network or into a save.
    ///
    /// <b>Cheap enough to call on a schedule.</b> One interface property read and a switch. The
    /// session component still resolves it once, and the editor re-reads it when it opens, so a
    /// language changed in the options is picked up by the next screen without a restart.
    /// </summary>
    public static class GameLanguage
    {
        /// <summary>
        /// Applies the game's language to <see cref="VgsText"/>. Returns true when the language is
        /// known — either because it was just read or because it is already in effect — so a
        /// caller that retries until the session exists can stop.
        ///
        /// Never throws: an unavailable session, a missing config or a game enum this build does
        /// not know leaves the mod on the language it already had.
        /// </summary>
        public static bool Apply()
        {
            try
            {
                IMySession session = MyAPIGateway.Session;
                if (session == null) return false;

                IMyConfig config = session.Config;
                if (config == null) return false;

                MyLanguagesEnum gameLanguage = config.Language;
                VgsLanguage language = Translate(gameLanguage);

                if (!VgsText.UseLanguage(language)) return true;

                // Logged only on a real change: this line is the answer to "why is the editor in
                // English" in a bug report, and it must not repeat every frame.
                VgsLog.Info("language " + language + " (game language " + gameLanguage + ")");
                return true;
            }
            catch (Exception e)
            {
                // Its own throttle tag, not the caller's: two call sites sharing one tag silence
                // each other, and these two fail for different reasons (this one cannot read the
                // config, the caller's cannot call this one at all).
                VgsLog.Throttled("language-read", "could not read the game language; keeping " + VgsText.Language, e, 600);
                return false;
            }
        }

        /// <summary>
        /// The mod translates two of the game's languages, so everything else falls back to
        /// English. <b>Not</b> a guess at the player's preference: mapping, say, Ukrainian to
        /// Russian would be a product decision this mod has no business making silently.
        /// </summary>
        private static VgsLanguage Translate(MyLanguagesEnum gameLanguage)
        {
            switch (gameLanguage)
            {
                case MyLanguagesEnum.Russian:
                    return VgsLanguage.Russian;
                default:
                    return VgsLanguage.English;
            }
        }
    }
}
