using System;
using Vgs.Core;

namespace Vgs.Game
{
    /// <summary>
    /// The clipboard for the editor's search field (ADR-0024, ADR-0027).
    ///
    /// <b>This is the editor's own buffer, not the operating system's.</b> That is a platform
    /// limit, and it was measured the hard way rather than assumed:
    ///
    ///  * `Sandbox.Graphics.MyClipboardService.GetText()` lives in a namespace the IL whitelist
    ///    does not admit.
    ///  * `VRage.Platform.Windows.Forms.MyClipboardHelper` is public and does have
    ///    `GetClipboardText()`, but its namespace is not admitted either.
    ///  * `VRage.MyVRage.Platform.System.Clipboard` — the indirection through the whitelisted
    ///    `VRage` namespace — **does not work for a mod**. The game refuses to load the assembly:
    ///    "The type or member 'IVRageSystem' is prohibited", likewise `IVRagePlatform` and
    ///    `MyVRage`. Verified on a real build on 2026-11-23, see ADR-0027.
    ///
    /// The root `VRage` namespace is listed in the whitelist, but it does not grant the types
    /// directly inside it; each of those that a mod may use is listed by name. This class therefore
    /// references no game type at all, which is also why it cannot break the mod's load again.
    ///
    /// Consequence, stated plainly in the UI hint: Ctrl+C / Ctrl+X / Ctrl+V move text **within the
    /// search field**. Nothing reaches another application, and text copied elsewhere cannot be
    /// pasted in. That is the whole of what the platform allows.
    ///
    /// Everything here is guarded: it is called from a key handler, and an exception escaping one
    /// would reach the session update.
    /// </summary>
    public static class TextClipboard
    {
        /// <summary>
        /// Last text copied or cut in this editor. The only source a paste has.
        /// </summary>
        private static string _buffer = string.Empty;

        /// <summary>
        /// What a paste would insert, or an empty string when there is nothing to paste.
        ///
        /// Read on demand rather than cached by the caller: a cut must be visible to the very next
        /// paste, and a copy of the caller's own state would be a second place to keep in sync.
        /// </summary>
        public static string PasteText
        {
            get { return _buffer; }
        }

        /// <summary>
        /// Remembers <paramref name="text"/> as what a paste will insert.
        ///
        /// Sanitised on the way in, so what a paste inserts is always one line of displayable text
        /// — the field is single-line, and a value that could not be displayed would look like a
        /// paste that silently dropped characters.
        /// </summary>
        public static void Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _buffer = Sanitize(text);
        }

        /// <summary>Forgets the buffer. Called when the session unloads.</summary>
        public static void Release()
        {
            _buffer = string.Empty;
        }

        /// <summary>
        /// Collapses every control character, so what is pasted is one line of text.
        ///
        /// A tab becomes a space rather than disappearing: text copied out of a table or a console
        /// would otherwise lose the separation between its words. Control characters are dropped
        /// because the field is single-line and cannot display them.
        /// </summary>
        private static string Sanitize(string text)
        {
            bool clean = true;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsControl(text[i]) && text[i] != ' ') { clean = false; break; }
            }
            if (clean) return text;

            var builder = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\t') builder.Append(' ');
                else if (!char.IsControl(c)) builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
