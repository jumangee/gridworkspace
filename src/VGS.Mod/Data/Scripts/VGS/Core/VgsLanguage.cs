using System;

namespace Vgs.Core
{
    /// <summary>
    /// A language the editor itself can speak (ADR-0033).
    ///
    /// <b>This is the editor's own list, not the game's.</b> The game has twenty-seven
    /// <c>MyLanguagesEnum</c> values, of which the mod translates two; a table for a language the
    /// mod has no text for would be twenty-seven near-copies of the English one. The translation
    /// from the game's value to this one happens in exactly one place,
    /// <c>Vgs.Game.GameLanguage.Translate</c>, and everything below that point speaks in terms of
    /// this enum — so adding a third language means a table in <see cref="VgsStrings"/>, a member
    /// here, and one line in that mapping.
    ///
    /// The numeric values are explicit and must not be reordered: <see cref="English"/> is the
    /// fallback the mod starts with, before any session exists to ask.
    /// </summary>
    public enum VgsLanguage
    {
        /// <summary>The default, and the fallback for every game language without its own table.</summary>
        English = 0,

        /// <summary>Russian.</summary>
        Russian = 1
    }
}
