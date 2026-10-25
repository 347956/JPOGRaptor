using GameNetcodeStuff;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

namespace JPOGRaptor.src
{
    public class RaptorPounceHelper(JPOGRaptorAI jPOGRaptorAI)
    {

        public const float PounceRange = 10f;

        private Coroutine? pounceCoroutine;
        private readonly JPOGRaptorAI jpogRaptorAI = jPOGRaptorAI;
        private readonly float totalPounceTime = 4.5f; // safety timeout for pounce
        private readonly float pounceDamagePeriod = 2.5f; // safety timeout for pounce
        private readonly int raptorId = jPOGRaptorAI.RaptorId;

        private Vector3 pounceDirection;

        public bool IsPouncing { get; private set; } = false;
        public bool PounceAttackDamage { get; private set; } = false;
        public float TimeSincePounceAttack { get; private set; } = 10f;
        public bool PounceAttackComplete { get; private set; } = false;
        public float PounceTimer { get; private set; } = 0f;
        public float PounceDuration { get; private set; } = 1f;
        public float PounceSpeed { get; private set; } = 15;
        public bool InRangeForPounceAttack { get; private set; } = false;

        // Every client only affects its own local player, so one flag per client is enough
        private bool localPlayerHitThisPounce = false;
        private int pounceTargetPlayerId = -1;
        private float hitBoxCheckTimer = 0f;
        private const float HitBoxCheckInterval = 0.1f;

        private Vector3 pounceTargetPlayerVelocity;
        private Vector3 pounceTargetPlayerlastPosition;
        private PlayerControllerB? velocityTrackedPlayer;
        private readonly float pouncePredictionTime = 1.5f;

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        // These are checked every AI interval, so log them at most once per LogThrottleSeconds
        private const float LogThrottleSeconds = 2f;
        private float lastCooldownLogTime = float.NegativeInfinity;
        private float lastBlockedLineLogTime = float.NegativeInfinity;

        [Conditional("DEBUG")]
        private void LogThrottled(ref float lastLogTime, string text)
        {
            if (Time.time - lastLogTime < LogThrottleSeconds) return;
            lastLogTime = Time.time;
            LogIfDebugBuild(text);
        }

        public void StartPounce()
        {
            if (IsPouncing) return;
            pounceCoroutine = jpogRaptorAI.StartCoroutine(PounceRoutine());
        }

        private IEnumerator PounceRoutine()
        {
            //jpogRaptorAI.agent.enabled = false; // turn off NavMeshAgent when moving manually
            IsPouncing = true;
            PounceAttackDamage = true;
            PounceAttackComplete = false;
            TimeSincePounceAttack = Time.deltaTime;

            // Predict future player position
            Vector3 predictedPosition = jpogRaptorAI.targetPlayer != null
            ? jpogRaptorAI.targetPlayer.transform.position + pounceTargetPlayerVelocity * pouncePredictionTime
            : jpogRaptorAI.transform.forward * 5f;

            // Keep the pounce on the ground plane so the raptor isn't pushed into or above the floor
            pounceDirection = predictedPosition - jpogRaptorAI.transform.position;
            pounceDirection.y = 0f;
            pounceDirection = pounceDirection.normalized;

            // Tell every client who this pounce is aimed at (-1 = nobody)
            int pounceTargetId = jpogRaptorAI.targetPlayer != null ? (int)jpogRaptorAI.targetPlayer.playerClientId : -1;
            jpogRaptorAI.BeginPounceClientRpc(pounceTargetId);
            hitBoxCheckTimer = 0f;

            // Play pounce animation on all clients
            jpogRaptorAI.DoAnimationClientRpc("pounceAttack");
            LogIfDebugBuild($"Raptor[{raptorId}]: Pounce started toward {predictedPosition}");

            // Wait for damage window duration
            yield return new WaitForSeconds(pounceDamagePeriod);
            PounceAttackDamage = false;

            // Wait for rest of pounce to complete
            float timer = 0f;
            while (timer < totalPounceTime - pounceDamagePeriod && jpogRaptorAI.currentBehaviourStateIndex == (int)JPOGRaptorAI.State.AttackingPlayer)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            // If state switched away, exit safely
            if (jpogRaptorAI.currentBehaviourStateIndex != (int)JPOGRaptorAI.State.AttackingPlayer)
            {
                LogIfDebugBuild($"Raptor[{raptorId}]: Pounce aborted because state changed");
                jpogRaptorAI.DropBodyInMouthServerRpc();
                ResetPounceFlags();
                yield break;
            }
            // Perform any wrap up: drop body, clean up
            jpogRaptorAI.DropBodyInMouthServerRpc();

            // Reset pounce flags
            ResetPounceFlags();
            //jPOGRaptorAI.SetAnimationBoolClientRPC("pounceComplete", true);
        }


        private void ResetPounceFlags()
        {

            LogIfDebugBuild($"Raptor[{raptorId}]: resetting pounce flags");
            //jpogRaptorAI.agent.enabled = true;
            //jpogRaptorAI.agent.Warp(jpogRaptorAI.transform.position);
            //jpogRaptorAI.agent.speed = 8f; // manually set speed again the prevent a jitter in the locomotion blend tree
            // Manually set the destination to prevent the navmesh agent from not having a destination set after the pounce ends
/*            if (jpogRaptorAI.targetPlayer != null)
            {
                jpogRaptorAI.agent.SetDestination(jpogRaptorAI.targetPlayer.transform.position);
            }*/

            IsPouncing = false;
            PounceAttackDamage = false;
            PounceAttackComplete = true;
            PounceTimer = 0f;
            TimeSincePounceAttack = 0f;
            InRangeForPounceAttack = false;
            LogIfDebugBuild($"Raptor[{raptorId}]: Pounce flags reset");
        }

        public void CancelPounce()
        {
            if (pounceCoroutine != null)
            {
                jpogRaptorAI.StopCoroutine(pounceCoroutine);
                pounceCoroutine = null;
            }
            IsPouncing = false;
        }

        private void PounceTargetting(PlayerControllerB? targetPlayer)
        {
            if (targetPlayer == null || Time.deltaTime <= 0f) return;

            // Restart the tracking when the target changes, otherwise the first velocity sample is huge
            if (velocityTrackedPlayer != targetPlayer)
            {
                velocityTrackedPlayer = targetPlayer;
                pounceTargetPlayerVelocity = Vector3.zero;
                pounceTargetPlayerlastPosition = targetPlayer.transform.position;
                return;
            }

            pounceTargetPlayerVelocity = (targetPlayer.transform.position - pounceTargetPlayerlastPosition) / Time.deltaTime;
            pounceTargetPlayerlastPosition = targetPlayer.transform.position;
        }

        private void UpdatePounceTimer()
        {
            this.PounceTimer += Time.deltaTime;
        }
        public void UpdateTimeSincePounceAttack()
        {
            if (IsPouncing) return;
            TimeSincePounceAttack += Time.deltaTime;
        }

        public void MoveRaptorDuringPounce(PlayerControllerB? targetPlayer)
        {
            if (targetPlayer == null) return;
            // Track the target's velocity continuously (not only while pouncing) so the prediction at pounce start is valid
            PounceTargetting(targetPlayer);
            if (!IsPouncing) return;
            jpogRaptorAI.turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);

            if (PounceAttackDamage)
            {
                hitBoxCheckTimer -= Time.deltaTime;
                if (hitBoxCheckTimer <= 0f)
                {
                    hitBoxCheckTimer = HitBoxCheckInterval;
                    jpogRaptorAI.CheckRaptorPounceHitBoxesClientRPC();
                }
            }
            UpdatePounceTimer();
            if (PounceTimer < PounceDuration)
            {
                //LogIfDebugBuild($"JPOGRaptor[{raptorId}]: pouncing towards target player");
                // Stop at the navmesh edge so the pounce can't carry the raptor through walls or off the map
                Vector3 step = pounceDirection * PounceSpeed * Time.deltaTime;
                Vector3 currentPosition = jpogRaptorAI.transform.position;
                if (!NavMesh.Raycast(currentPosition, currentPosition + step, out _, jpogRaptorAI.agent.areaMask))
                {
                    jpogRaptorAI.transform.position = currentPosition + step;
                }
            }
        }

        // Checks if player are in range for a pounce attack
        public void CheckIfPlayersAreInRangeForPounceAttack()
        {
            // Re-evaluated on every check so a stale "in range" result can't trigger a pounce from afar
            InRangeForPounceAttack = false;
            if (jpogRaptorAI.targetPlayer != null)
            {
                float distance = Vector3.Distance(jpogRaptorAI.transform.position, jpogRaptorAI.targetPlayer.transform.position);
                if (distance < PounceRange && HasClearLineToPlayer())
                {
                    if (TimeSincePounceAttack >= 10f)
                    {
                        InRangeForPounceAttack = true;
                        LogIfDebugBuild($"Raptor[{raptorId}]: Player in range and line clear for pounce!");
                    }
                    else
                    {
                        LogThrottled(ref lastCooldownLogTime, $"JPOGRaptor[{raptorId}]: target player was in pounce range, but pounce is on cooldown");
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the direct line to the target player is clear (no walls or obstacles).
        /// </summary>
        private bool HasClearLineToPlayer()
        {
            var player = jpogRaptorAI.targetPlayer;
            if (player == null) return false;

            Vector3 from = jpogRaptorAI.transform.position + Vector3.up * 1f;
            Vector3 to = player.transform.position + Vector3.up * 1f;

            // Only level geometry blocks the line: the mask excludes the player and enemy layers (so neither the target nor
            // the raptor's own colliders count) and triggers are ignored
            bool blocked = Physics.Linecast(from, to, out RaycastHit hit, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore);
            if (blocked)
            {
                LogThrottled(ref lastBlockedLineLogTime, $"Raptor[{raptorId}]: Line to player blocked by [{hit.collider.name}]");
            }

            return !blocked;
        }

        // Runs on every client when a pounce starts
        public void BeginPounce(int targetPlayerId)
        {
            pounceTargetPlayerId = targetPlayerId;
            localPlayerHitThisPounce = false;
        }

        // Checks the for players in the pounce hitbox
        public void CheckRaptorPounceHitBoxes()
        {
            if (localPlayerHitThisPounce) return;

            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity

            Collider[] hitColliders = Physics.OverlapBox(
                jpogRaptorAI.attackArea.position,
                jpogRaptorAI.attackArea.localScale,
                jpogRaptorAI.attackArea.rotation, playerLayer
                );

            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = jpogRaptorAI.MeetsStandardPlayerCollisionConditions(player);
                    // Only the player this pounce was aimed at can be hit
                    if (playerControllerB != null && (int)playerControllerB.playerClientId == pounceTargetPlayerId)
                    {
                        localPlayerHitThisPounce = true;
                        int chosenDeathAnimation = (UnityEngine.Random.value < 0.9f) ? 0 : 9;
                        if (jpogRaptorAI.CarryingKilledPlayerBody == null)
                        {
                            int playerId = (int)playerControllerB.playerClientId;
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Hit player [{playerId}]");
                            //jpogRaptorAI.SpawnPlayerBloodOnClientRpc(playerControllerB.NetworkObjectId, Vector3.zero);
                            playerControllerB.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Mauling, chosenDeathAnimation);
                            jpogRaptorAI.TakeBodyInMouthServerRpc(playerId);
                        }
                        else
                        {
                            //jpogRaptorAI.SpawnPlayerBloodOnClientRpc(playerControllerB.NetworkObjectId, Vector3.zero);
                            playerControllerB.DamagePlayer(40);
                        }
                    }
                }
            }
        }
    }
}
