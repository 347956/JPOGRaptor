using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace JPOGRaptor.src
{
    internal class RaptorTargetingHelper
    {

        private readonly JPOGRaptorAI jPOGRaptorAI;
        private readonly int raptorId;
        public float MaxChaseDistance { get; private set; } = 30f;

        public RaptorTargetingHelper(JPOGRaptorAI jPOGRaptorAI)
        {
            this.jPOGRaptorAI = jPOGRaptorAI;
            this.raptorId = jPOGRaptorAI.raptorId;
        }


        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            TargetClosestPlayer(true);

            if (jPOGRaptorAI.targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(false);
                range = senseRange;
            }
            return jPOGRaptorAI.targetPlayer != null && Vector3.Distance(jPOGRaptorAI.transform.position, jPOGRaptorAI.targetPlayer.transform.position) < range;
        }

        public bool TargetClosestPlayer(bool requireLOS)
        {
            float closestDist = float.MaxValue;
            jPOGRaptorAI.targetPlayer = null;
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                float dist = Vector3.Distance(jPOGRaptorAI.transform.position, player.transform.position);
                if (dist < closestDist && (!requireLOS || jPOGRaptorAI.CheckLineOfSightForPosition(player.transform.position)))
                {
                    closestDist = dist;
                    jPOGRaptorAI.targetPlayer = player;
                }
            }
            return jPOGRaptorAI.targetPlayer != null;
        }

        // This should check if the player is still targetable for the raptor given the target player is inside the ship
        // If the ship doors are closed, but the raptor is inside the ship: the target player is still valid
        // If the ship doors are closed, but the raptor is outside the ship: the target player is no longer valid
        public bool CheckIfTargetCanBeReachedInsideShip()
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Check: PlayerInShip={jPOGRaptorAI.targetPlayer.isInHangarShipRoom}, DoorsClosed={StartOfRound.Instance.hangarDoorsClosed}, RaptorInside={jPOGRaptorAI.isInsidePlayerShip}");
            // Safe null check FIRST
            if (jPOGRaptorAI.targetPlayer == null) return false;

            // If the player is NOT inside the hangar ship room, it’s reachable
            if (!jPOGRaptorAI.targetPlayer.isInHangarShipRoom) return true;

            // If doors open, it’s reachable
            if (StartOfRound.Instance.hangarDoorsClosed) return true;

            // If raptor is inside, it’s reachable
            if (jPOGRaptorAI.isInsidePlayerShip) return true;

            // Else: player is inside ship, doors closed, raptor outside: not reachable
            return false;
        }

        public bool PlayerHasHorizontalLOS(PlayerControllerB player)
        {
            Vector3 to = jPOGRaptorAI.transform.position - player.transform.position;
            to.y = 0f;
            return Vector3.Angle(player.transform.forward, to) < 68f;
        }

        public bool IsTargetTooFar()
        {
            return jPOGRaptorAI.targetPlayer != null && Vector3.Distance(jPOGRaptorAI.transform.position, jPOGRaptorAI.targetPlayer.transform.position) > MaxChaseDistance;
        }

        public bool EnsureTarget()
        {
            if (jPOGRaptorAI.targetPlayer == null)
            {
                // Try finding any valid target, no LOS required
                TargetClosestPlayer(false);
            }
            return jPOGRaptorAI.targetPlayer != null;
        }
    }
}
