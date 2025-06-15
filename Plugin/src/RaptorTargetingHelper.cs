using GameNetcodeStuff;
using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;

namespace JPOGRaptor.src
{
    internal class RaptorTargetingHelper
    {

        private readonly JPOGRaptorAI jPOGRaptorAI;
        private readonly int raptorId;
        public float MaxChaseDistance { get; private set; } = 30f;
        public float TimeWithoutPath { get; private set; } = 0f;

        private float lastTimeoutUpdate;

        public float MaxUnreachableTime { get; private set; } = 7f;

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
            PlayerControllerB? bestTarget = null;

            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null) continue;


                float dist = Vector3.Distance(jPOGRaptorAI.transform.position, player.transform.position);

                bool hasLOS = !requireLOS || jPOGRaptorAI.CheckLineOfSightForPosition(player.transform.position);
                bool reachable = CheckIfTargetCanBeReachedInsideShip(player);

                if (dist < closestDist && hasLOS && reachable)
                {
                    if (!CheckIfTargetCanBeReachedInsideShip(player)) continue;

                    bestTarget = player;
                    closestDist = dist;
                }
            }
            jPOGRaptorAI.targetPlayer = bestTarget;

            return bestTarget != null;
        }
        /// <summary>
        /// Combines the path + Ship reachability check with the chase time-out check
        /// If the path is unreachable and the time-out period is reached returns fasle
        /// false = I cannot reach the target and I tried for x amount of time
        /// </summary>
        public bool CheckIfTargetPlayerIsReachable(PlayerControllerB playerToCheck)
        {
            bool reachable = CheckTargetPathReachability(playerToCheck);
            bool timeOut = HasTimedOutUnreachable();
            if (!reachable && timeOut)            
            {
                LogIfDebugBuild($"Raptor[{raptorId}]: target reachability > reachable = [{reachable}] time out = [{timeOut}]");
                TimeWithoutPath = 0f;
                return false;
            }
            return true;
        }


        /// <summary>
        /// Checks whether the target player is currently reachable, considering navmesh & ship door.
        /// Updates the timeout timer if not reachable.
        /// Returns true if target is reachable; false if not.
        /// </summary>
        public bool CheckTargetPathReachability(PlayerControllerB playerToCheck)
        {

            NavMeshPath testPath = new NavMeshPath();
            bool pathExists = NavMesh.CalculatePath(
                jPOGRaptorAI.agent.transform.position,
                playerToCheck.transform.position,
                NavMesh.AllAreas,
                testPath
            );

            bool pathComplete = pathExists && testPath.status == NavMeshPathStatus.PathComplete;

            // Ship door status
            bool reachableByDoors = CheckIfTargetCanBeReachedInsideShip(playerToCheck);

            return pathComplete && reachableByDoors;
        }

        public void UpdatePathTimeout(PlayerControllerB player)
        {
            bool reachable = CheckTargetPathReachability(player);
            if (reachable)
            {
                TimeWithoutPath = 0f;
                lastTimeoutUpdate = Time.time;
            }
            else
            {
                if(Time.time - lastTimeoutUpdate >= 1f)
                {
                    TimeWithoutPath += 1;
                    lastTimeoutUpdate = Time.time;
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: uppdated time out value = [{TimeWithoutPath}]");
                }
            }
        }

        /// <summary>
        /// This should check if the player is still targetable for the raptor given the target player is inside the ship
        /// If the ship doors are closed, but the raptor is inside the ship: the target player is still valid
        /// If the ship doors are closed, but the raptor is outside the ship: the target player is no longer valid
        /// </summary>

        private bool CheckIfTargetCanBeReachedInsideShip(PlayerControllerB playerToCheck)
        {
            //LogIfDebugBuild($"Raptor[{raptorId}]: Check: PlayerInShip={playerToCheck.isInHangarShipRoom}, DoorsClosed={StartOfRound.Instance.hangarDoorsClosed}, RaptorInside={jPOGRaptorAI.isInsidePlayerShip}");

            if (playerToCheck == null) return false;

            // If the player is NOT inside ship → reachable
            if (!playerToCheck.isInHangarShipRoom)
                return true;

            // If doors are OPEN → reachable
            if (!StartOfRound.Instance.hangarDoorsClosed)
                return true;

            // If raptor is already inside → reachable
            if (jPOGRaptorAI.isInsidePlayerShip)
                return true;

            // Otherwise: player is in ship, doors are closed, raptor is outside → NOT reachable
            return false;
        }

        /// <summary>
        /// Returns true if the target has been unreachable for too long.
        /// </summary>
        public bool HasTimedOutUnreachable()
        {
            return TimeWithoutPath >= MaxUnreachableTime;
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

        public void TargetPlayerWhoHit(PlayerControllerB playerWhoHit)
        {
            if (jPOGRaptorAI.isEnemyDead) return; // Extra check just in case it gets called after the raptor is dead

            if (playerWhoHit != null) // Extra null check for safety
            {
                jPOGRaptorAI.targetPlayer = playerWhoHit;
                if (jPOGRaptorAI.currentBehaviourStateIndex == (int)JPOGRaptorAI.State.SearchingForPlayer)
                {
                    jPOGRaptorAI.SwitchToBehaviourServerRpc((int)JPOGRaptorAI.State.ChasingPlayer);
                }
                else if (jPOGRaptorAI.currentBehaviourStateIndex == (int)JPOGRaptorAI.State.ChasingPlayer)
                {
                    // Already chasing: immediately update path to new target
                    jPOGRaptorAI.SetDestinationToPosition(jPOGRaptorAI.targetPlayer.transform.position);
                }
            }
        }
    }
}
