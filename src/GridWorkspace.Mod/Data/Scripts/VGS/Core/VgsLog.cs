using System;
using System.Collections.Generic;
using System.Text;
using VRage.Utils;

namespace Vgs.Core
{
    /// <summary>
    /// Central logging facility for the mod.
    ///
    /// Why not call MyLog.Default.WriteLine directly everywhere:
    ///  * a graph editor can produce a lot of per-frame noise, and the game log is
    ///    shared with everything else, so every call site would need its own throttling;
    ///  * exceptions inside a mod update loop are swallowed by the game in ways that are
    ///    hard to trace, so errors must be routed through one place that prefixes them
    ///    with a stable tag we can grep for.
    ///
    /// Log lines are prefixed with <see cref="Tag"/> so a bug report can be reduced to
    /// "grep VGS" in %APPDATA%\SpaceEngineers\SpaceEngineers_*.log.
    ///
    /// Throttling is keyed by a caller-supplied tag, not global. A single shared timer would
    /// make independent failures suppress each other: the first broken draw path would hide
    /// every other one behind the same window.
    /// </summary>
    public static class VgsLog
    {
        public const string Tag = "[VGS]";

        private static readonly StringBuilder Scratch = new StringBuilder(256);

        /// <summary>
        /// Value of the tag's own call counter when it last emitted a line.
        ///
        /// An attempt count, not a frame number: frames stop while the game is paused and the
        /// editor pauses it (ADR-0026). Read against <see cref="Calls"/>, never against a clock.
        /// </summary>
        private static readonly Dictionary<string, int> LastEmit = new Dictionary<string, int>(8);

        /// <summary>How many calls each throttle tag suppressed since its last line.</summary>
        private static readonly Dictionary<string, int> Suppressed = new Dictionary<string, int>(8);

        private static bool _verbose;

        /// <summary>
        /// Enables per-frame diagnostics. Keep this false in shipped builds: the editor
        /// redraws every frame while open, and unthrottled logging measurably hurts.
        /// </summary>
        public static bool Verbose
        {
            get { return _verbose; }
            set
            {
                if (_verbose == value) return;
                _verbose = value;
                Info(value ? "verbose logging ON" : "verbose logging OFF");
            }
        }

        public static void Info(string message)
        {
            Write(message);
        }

        public static void Warn(string message)
        {
            Write(message);
        }

        public static void Error(string message)
        {
            Write(message);
        }

        public static void Error(string message, Exception exception)
        {
            if (exception == null)
            {
                Error(message);
                return;
            }

            // Full stack trace on errors only: this is where the cost is worth paying.
            Write(message + " :: " + exception.GetType().Name + ": " +
                  exception.Message + Environment.NewLine + exception.StackTrace);
        }

        /// <summary>
        /// Logs at most once per <paramref name="intervalAttempts"/> calls **of this tag**.
        /// Use for conditions that can be true every frame.
        /// </summary>
        /// <remarks>
        /// The tag is the throttle identity: two call sites must never share one, or they
        /// silence each other.
        /// </remarks>
        public static void Throttled(string tag, string message, int intervalAttempts)
        {
            if (!TryBeginThrottled(tag, intervalAttempts)) return;

            int suppressed;
            if (Suppressed.TryGetValue(tag, out suppressed) && suppressed > 0)
            {
                Write(message + " (+" + suppressed + " similar suppressed)");
                Suppressed[tag] = 0;
                return;
            }

            Write(message);
        }

        /// <summary>
        /// Throttled variant for exceptions in hot paths.
        ///
        /// Takes the exception rather than a formatted message so a per-frame failure path
        /// allocates nothing until the throttle actually lets a line through.
        /// </summary>
        public static void Throttled(string tag, string prefix, Exception exception, int intervalAttempts)
        {
            if (!TryBeginThrottled(tag, intervalAttempts)) return;

            int suppressed;
            if (Suppressed.TryGetValue(tag, out suppressed) && suppressed > 0)
            {
                Write(prefix + " (+" + suppressed + " similar suppressed) :: " +
                      (exception == null ? "unknown" : exception.GetType().Name + ": " + exception.Message));
                Suppressed[tag] = 0;
                return;
            }

            Write(prefix + " :: " + (exception == null ? "unknown" : exception.GetType().Name + ": " + exception.Message));
        }

        public static void Debug(string message)
        {
            if (!_verbose) return;
            Write(message);
        }

        /// <summary>
        /// Returns true when this tag is allowed to emit now, and counts the call as
        /// suppressed otherwise. Shared by both Throttled overloads.
        ///
        /// <b>Counted in attempts of this tag, not in session frames</b> (ADR-0026).
        /// `VgsSession.Frame` only advances from `UpdateAfterSimulation`, which a paused session
        /// skips — and the editor pauses the game. A frame-based throttle therefore froze for the
        /// whole time the editor was open, so every tag emitted once and then went silent for the
        /// rest of the screen: exactly where the log is the primary diagnostic, and exactly where
        /// the new failure modes (clipboard read/write, ADR-0025) fire.
        ///
        /// <b>Per tag, not global.</b> A single shared counter would couple the tags: a chatty one
        /// advances the clock for everybody, so a tag with interval 300 could emit every third
        /// frame instead of every three hundredth. The graph builder logs per block, which makes
        /// that reachable — a hundred-block refresh advances a global counter four hundred times in
        /// one frame. Counting each tag's own calls keeps every interval meaning what it says, at
        /// the cost of one dictionary entry per tag (they already exist).
        /// </summary>
        private static bool TryBeginThrottled(string tag, int intervalAttempts)
        {
            if (tag == null) tag = "default";
            if (intervalAttempts < 1) intervalAttempts = 1;

            int calls;
            Calls.TryGetValue(tag, out calls);

            // Saturating: a session can run for a very long time, and wrapping into negative would
            // make this tag permanently eligible instead of merely mistimed.
            if (calls < int.MaxValue) calls++;
            Calls[tag] = calls;

            int last;
            if (LastEmit.TryGetValue(tag, out last) && calls - last < intervalAttempts)
            {
                int suppressed;
                Suppressed.TryGetValue(tag, out suppressed);
                Suppressed[tag] = suppressed + 1;
                return false;
            }

            LastEmit[tag] = calls;
            return true;
        }

        /// <summary>
        /// How many times each tag has been offered to the throttle. The throttle's clock.
        ///
        /// Attempts rather than frames for the reason in <see cref="TryBeginThrottled"/>: attempts
        /// keep coming while the game is paused, frames do not. Per tag so that one chatty call
        /// site cannot shorten another's interval.
        /// </summary>
        private static readonly Dictionary<string, int> Calls = new Dictionary<string, int>();

        private static void Write(string message)
        {
            if (message == null) message = "<null>";

            Scratch.Clear();
            Scratch.Append(Tag).Append(' ').Append(message);

            // MyLog is null during the earliest part of session start; never let logging
            // itself be the reason the mod fails to load.
            try
            {
                MyLog.Default.WriteLineAndConsole(Scratch.ToString());
            }
            catch
            {
                // Intentionally ignored: a logging failure must never propagate into the
                // game loop. There is nowhere left to report it.
            }
        }

        /// <summary>
        /// Drops throttle state. Called on session unload so counters from a previous session
        /// cannot silence lines in the next one.
        /// </summary>
        internal static void Reset()
        {
            LastEmit.Clear();
            Suppressed.Clear();
            Calls.Clear();
            _verbose = false;
        }
    }
}
