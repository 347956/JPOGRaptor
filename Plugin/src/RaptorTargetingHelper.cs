using GameNetcodeStuff;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

namespace JPOGRaptor.src
{
    public class RaptorTargetingHelper(JPOGRaptorAI jPOGRaptorAI)
    {

        private readonly JPOGRaptorAI jPOGRaptorAI = jPOGRaptorAI;
        private readonly int raptorId = jPOGRaptorAI.RaptorId;
        // A player this close is always targeted, even in a group or out of sight
        private const float SenseRange = 5f;
        // A player in view within this distance is targeted when alone
        private const float SpotRange = 40f;
        // Another living player within this distance of a player means that player is not alone
        private const float GroupRadius = 10f;
        // Must stay above SpotRange, otherwise a spotted player is dropped again as "too far" when the raptor starts chasing
        public float MaxChaseDistance { get; private set; } = 45f;
        // How wide a player's view is when checking if the raptor is seen (half angle in degrees)
        private const float SeenViewAngle = 45f;
        public float TimeWithoutPath { get; private set; } = 0f;

        private float lastTimeoutUpdate;
        private bool lastPathReachable = true;

        public float MaxUnreachableTime { get; private set; } = 7f;

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public enum AcquireResult
        {
            None,
            Spotted,
            TooClose
        }

        /// <summary>
        /// Picks a target for a searching raptor.
        /// A player within sense range is always targeted (TooClose), even in a group or without line of sight.
        /// A player in view within spot range is only targeted when alone (Spotted).
        /// The current target is left alone when nobody qualifies.
        /// </summary>
        public AcquireResult TryAcquireTarget(bool allowSpotting = true)
        {
            PlayerControllerB? tooClose = FindClosestPlayer(false, SenseRange);
            if (tooClose != null)
            {
                jPOGRaptorAI.targetPlayer = tooClose;
                return AcquireResult.TooClose;
            }

            if (!allowSpotting) return AcquireResult.None;

            PlayerControllerB? spotted = FindClosestPlayer(true, SpotRange, true);
            if (spotted != null)
            {
                jPOGRaptorAI.targetPlayer = spotted;
                return AcquireResult.Spotted;
            }
            return AcquireResult.None;
        }

        /// <summary>
        /// True when no other living player is within GroupRadius of the given player.
        /// </summary>
        public bool IsAlone(PlayerControllerB player)
        {
            var distances = new List<float>();
            foreach (var other in StartOfRound.Instance.allPlayerScripts)
            {
                if (other == null || other == player) continue;
                if (!other.isPlayerControlled || other.isPlayerDead) continue;
                distances.Add(Vector3.Distance(other.transform.position, player.transform.position));
            }
            return RaptorRules.IsAlone(distances, GroupRadius);
        }

        /// <summary>
        /// True when any living player within range has the raptor in their view (view cone and line of sight).
        /// </summary>
        public bool IsSeenByAnyPlayer(float range)
        {
            Vector3 eye = jPOGRaptorAI.transform.position + Vector3.up * 1.2f;
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled || player.isPlayerDead) continue;
                if (player.HasLineOfSightToPosition(eye, SeenViewAngle, (int)range)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when no living player within range has a clear line to the point, regardless of where they look.
        /// </summary>
        public bool IsHiddenFromAllPlayers(Vector3 point, float range)
        {
            Vector3 target = point + Vector3.up * 1.2f;
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled || player.isPlayerDead) continue;
                if (Vector3.Distance(player.transform.position, point) > range) continue;
                if (!Physics.Linecast(player.playerEye.position, target, StartOfRound.Instance.collidersRoomDefaultAndFoliage, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        public bool TargetClosestPlayer(bool requireLOS)
        {
            PlayerControllerB? bestTarget = FindClosestPlayer(requireLOS);
            jPOGRaptorAI.targetPlayer = bestTarget;
            return bestTarget != null;
        }

        private PlayerControllerB? FindClosestPlayer(bool requireLOS, float? maxRange = null, bool requireAlone = false)
        {
            float range = maxRange ?? MaxChaseDistance;
            float closestDist = float.MaxValue;
            PlayerControllerB? bestTarget = null;

            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null) continue;

                // Skips dead players, unused player slots and players the outside raptor cannot target (e.g. inside the facility)
                if (!jPOGRaptorAI.PlayerIsTargetable(player, false, false)) continue;

                float dist = Vector3.Distance(jPOGRaptorAI.transform.position, player.transform.position);
                if (dist >= closestDist || dist > range) continue;

                if(requireLOS && !jPOGRaptorAI.CheckLineOfSightForPosition(player.transform.position)) continue;

                if (requireAlone && !IsAlone(player)) continue;

                // Includes the ship door check
                if (!CheckTargetPathReachability(player)) continue;

                bestTarget = player;
                closestDist = dist;
                //LogIfDebugBuild($"Raptor[{raptorId}]: Candidate {player.playerUsername}, dist={dist}, reachable={true}, hasLOS={true}");
            }
            return bestTarget;
        }
        /// <summary>
        /// Combines the path + Ship reachability check with the chase time-out check
        /// If the path is unreachable and the time-out period is reached returns fasle
        /// false = I cannot reach the target and I tried for x amount of time
        /// </summary>
        public bool CheckIfTargetPlayerIsReachable()
        {
            // Uses the result of the latest UpdatePathTimeout call, which must run first, to avoid a second path calculation
            bool reachable = lastPathReachable;
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

            NavMeshPath testPath = new();
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
            lastPathReachable = reachable;
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

            // Don't lock onto an attacker the raptor cannot reach (e.g. a player in the ship behind closed doors)
            if (playerWhoHit != null && CheckTargetPathReachability(playerWhoHit)) // Extra null check for safety
            {
                jPOGRaptorAI.targetPlayer = playerWhoHit;
                // Pounce right away when possible
                if (jPOGRaptorAI.TryInstantPounce()) return;

                if (jPOGRaptorAI.currentBehaviourStateIndex == (int)JPOGRaptorAI.State.SearchingForPlayer
                    || jPOGRaptorAI.currentBehaviourStateIndex == (int)JPOGRaptorAI.State.StalkingPlayer)
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

        public void HandleTargetUnreachability()
        {
            // Only perform this check if we currently have a target and are chasing
            if (jPOGRaptorAI.targetPlayer == null || jPOGRaptorAI.currentBehaviourStateIndex != (int)JPOGRaptorAI.State.ChasingPlayer)
            {
                return;
            }

            // Check if the current target is now unreachable
            bool targetIsUnreachable = IsTargetTooFar() ||
                                       !jPOGRaptorAI.targetPlayer.isPlayerControlled || // Target died or disconnected
                                       !CheckIfTargetCanBeReachedInsideShip(jPOGRaptorAI.targetPlayer);

            // If the target is unreachable, try to find a new one
            if (targetIsUnreachable)
            {
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Current target {jPOGRaptorAI.targetPlayer.playerUsername} is unreachable. Attempting to retarget.");

                RetargetOrSearch();
            }
        }

        /// <summary>
        /// Drops the current target and looks for a new reachable one, first with line of sight, then without.
        /// Switches to SearchingForPlayer when nobody is found.
        /// </summary>
        public void RetargetOrSearch()
        {
            PlayerControllerB? originalTarget = jPOGRaptorAI.targetPlayer; // Keep reference to original for logging
            jPOGRaptorAI.targetPlayer = null; // Clear current target to force a new search

            // First, try to find a new player with line of sight (more realistic chase)
            TargetClosestPlayer(true);

            if (jPOGRaptorAI.targetPlayer == null)
            {
                // If no LOS target, try to find any player within sensing distance
                TargetClosestPlayer(false);
            }

            if (jPOGRaptorAI.targetPlayer == null)
            {
                // If still no target, switch to searching state
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: No new target found. Switching to SearchingForPlayer state.");
                jPOGRaptorAI.SwitchToBehaviourServerRpc((int)JPOGRaptorAI.State.SearchingForPlayer);
            }
            else
            {
                // Successfully found a new target
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Retargeted from {originalTarget?.playerUsername} to {jPOGRaptorAI.targetPlayer.playerUsername}.");
                jPOGRaptorAI.SetDestinationToPosition(jPOGRaptorAI.targetPlayer.transform.position);
            }
        }
    }
}
