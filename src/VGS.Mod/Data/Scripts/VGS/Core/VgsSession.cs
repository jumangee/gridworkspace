using System;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;

namespace Vgs.Core
{
    /// <summary>
    /// Static facade over the running session: who we are, what tick it is, and which
    /// screen (if any) is open.
    ///
    /// Rationale: a mod has no dependency-injection container and no main loop of its own.
    /// Everything is reached through one <see cref="MySessionComponentBase"/> instance.
    /// Rather than passing that instance through every constructor of every subsystem
    /// (which forces every test to build a session), the subsystems read the handful of
    /// facts they need from here. The mutable surface is deliberately tiny.
    ///
    /// This class is NOT thread safe and is not meant to be: Space Engineers calls session
    /// components on the main thread, and any off-thread work must marshal back through
    /// <c>MyAPIGateway.Utilities.InvokeOnGameThread</c>.
    /// </summary>
    public static class VgsSession
    {
        private static MySessionComponentBase _component;

        /// <summary>Monotonic frame counter, incremented once per UpdateAfterSimulation.</summary>
        public static int Frame { get; private set; }

        /// <summary>True once <see cref="Init"/> has run and the session is usable.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>The mod's session component, or null before Init.</summary>
        public static MySessionComponentBase Component
        {
            get { return _component; }
        }

        /// <summary>True when running on a dedicated server, where no GUI may be created.</summary>
        public static bool IsDedicated
        {
            get
            {
                try { return MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated; }
                catch { return false; }
            }
        }

        /// <summary>True when a local player exists and can see a screen.</summary>
        public static bool HasLocalPlayer
        {
            get
            {
                try
                {
                    return MyAPIGateway.Session != null
                        && MyAPIGateway.Session.LocalHumanPlayer != null
                        && !IsDedicated;
                }
                catch { return false; }
            }
        }

        internal static void Bind(MySessionComponentBase component)
        {
            _component = component;
            IsReady = component != null;
            if (component == null) Frame = 0;
        }

        internal static void AdvanceFrame()
        {
            Frame++;
        }

        internal static void Reset()
        {
            _component = null;
            IsReady = false;
            Frame = 0;
        }
    }
}
