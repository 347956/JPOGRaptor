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
        /// A silent flee (spotted while stalking) restarts from Searching, a loud one (missed pounce) resumes the chase.
        /// </summary>
        public static bool ChasesAfterFlee(bool fleeWasSilent) => !fleeWasSilent;

        /// <summary>
        /// Time the player has been following a fleeing raptor: grows while the player is close, decays while not.
        /// </summary>
        public static float UpdatePursuedTime(float pursuedTime, bool playerIsClose, float deltaTime)
        {
            return playerIsClose ? pursuedTime + deltaTime : Math.Max(0f, pursuedTime - deltaTime);
        }
    }
}
