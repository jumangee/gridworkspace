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

        /// <summary>Last frame each throttle tag emitted a line.</summary>
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
        /// Logs at most once per <paramref name="intervalFrames"/> calls for the given
        /// <paramref name="tag"/>. Use for conditions that can be true every frame.
        /// </summary>
        /// <remarks>
        /// The tag is the throttle identity: two call sites must never share one, or they
        /// silence each other.
        /// </remarks>
        public static void Throttled(string tag, string message, int intervalFrames)
        {
            if (!TryBeginThrottled(tag, intervalFrames)) return;

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
        public static void Throttled(string tag, string prefix, Exception exception, int intervalFrames)
        {
            if (!TryBeginThrottled(tag, intervalFrames)) return;

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
        /// </summary>
        private static bool TryBeginThrottled(string tag, int intervalFrames)
        {
            if (tag == null) tag = "default";
            if (intervalFrames < 1) intervalFrames = 1;

            int last;
            if (LastEmit.TryGetValue(tag, out last) && VgsSession.Frame - last < intervalFrames)
            {
                int suppressed;
                Suppressed.TryGetValue(tag, out suppressed);
                Suppressed[tag] = suppressed + 1;
                return false;
            }

            LastEmit[tag] = VgsSession.Frame;
            return true;
        }

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
        /// Drops throttle state. Called on session unload so frame numbers from a previous
        /// session cannot silence lines in the next one.
        /// </summary>
        internal static void Reset()
        {
            LastEmit.Clear();
            Suppressed.Clear();
            _verbose = false;
        }
    }
}
