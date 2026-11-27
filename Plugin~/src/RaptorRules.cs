using System;
using System.Collections.Generic;

namespace JPOGRaptor.src
{
    /// <summary>
    /// Pure decision rules of the raptor AI. No Unity types, so they can be unit tested (see Tests/JPOGRaptor.Tests).
    /// </summary>
    public static class RaptorRules
    {
        /// <summary>
        /// True when none of the other living players is closer than groupRadius.
        /// </summary>
        public static bool IsAlone(IEnumerable<float> distancesToOtherLivingPlayers, float groupRadius)
        {
            foreach (float distance in distancesToOtherLivingPlayers)
            {
                if (distance < groupRadius) return false;
            }
            return true;
        }

        /// <summary>
        /// Strictly inside the radius. Used for the pack rule, the help call range and the flee-to-raptor search.
        /// </summary>
        public static bool IsWithinRadius(float distance, float radius) => distance < radius;

        /// <summary>
        /// True while the raptor must not start stalking again after a flee.
        /// </summary>
        public static bool IsStalkOnCooldown(float now, float lastFleeTime, float cooldown) => now - lastFleeTime < cooldown;

        /// <summary>
        /// Accumulated time of a condition (player is close, raptor is seen): grows while active, decays while not.
        /// </summary>
        public static float UpdateTimer(float timer, bool active, float deltaTime)
        {
            return active ? timer + deltaTime : Math.Max(0f, timer - deltaTime);
        }

        /// <summary>
        /// Hiding ends after hiddenRequired seconds out of sight once minTime has passed, or at the timeout.
        /// </summary>
        public static bool IsHideComplete(float timeSinceStart, float hiddenTime, float minTime, float hiddenRequired, float timeout)
        {
            return timeSinceStart >= timeout || (timeSinceStart >= minTime && hiddenTime >= hiddenRequired);
        }
    }
}
