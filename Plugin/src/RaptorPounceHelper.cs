using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace JPOGRaptor.src
{
    internal class RaptorPounceHelper
    {

        private readonly JPOGRaptorAI jPOGRaptorAI;
        private Coroutine? pounceCoroutine;

        private readonly float maxPounceTime = 4f; // safety timeout for pounce
        private readonly float totalPounceTime = 4.13f; // safety timeout for pounce
        private readonly float pounceDamagePeriod = 2.5f; // safety timeout for pounce
        private Vector3 pounceDirection;
        public bool IsPouncing { get; private set; } = false;
        private readonly int raptorId;
        public bool PounceAttackDamage { get; private set; } = false;
        public float TimeSincePounceAttack { get; private set; } = 10f;
        public bool PounceAttackComplete { get; private set; } = false;
        public float PounceTimer { get; private set; } = 0f;
        public float PounceDuration { get; private set; } = 1f;
        public float PounceSpeed { get; private set; } = 15;
        public bool InRangeForPounceAttack { get; private set; } = false;

        private Vector3 pounceTargetPlayerVelocity;
        private Vector3 pounceTargetPlayerlastPosition;
        private readonly float pouncePredictionTime = 1.5f;


        public RaptorPounceHelper(JPOGRaptorAI jPOGRaptorAI)
        {
            this.jPOGRaptorAI = jPOGRaptorAI;
            this.raptorId = jPOGRaptorAI.raptorId;
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public void StartPounce()
        {
            if (IsPouncing) return;
            pounceCoroutine = jPOGRaptorAI.StartCoroutine(pounceRoutine());
        }

        private IEnumerator pounceRoutine()
        {
            IsPouncing = true;
            PounceAttackDamage = true;
            PounceAttackComplete = false;
            TimeSincePounceAttack = Time.deltaTime;

            // Predict future player position
            Vector3 predictedPosition = jPOGRaptorAI.targetPlayer != null
            ? jPOGRaptorAI.targetPlayer.transform.position + pounceTargetPlayerVelocity * pouncePredictionTime
            : jPOGRaptorAI.transform.forward * 5f;

            pounceDirection = (predictedPosition - jPOGRaptorAI.transform.position).normalized;

            // Play pounce animation on all clients
            jPOGRaptorAI.DoAnimationClientRpc("pounceAttack");
            LogIfDebugBuild($"Raptor[{raptorId}]: Pounce started toward {predictedPosition}");

            // Wait for damage window duration
            yield return new WaitForSeconds(pounceDamagePeriod);
            PounceAttackDamage = false;

            // Wait for rest of pounce to complete
            float timer = 0f;
            while (timer < totalPounceTime - pounceDamagePeriod && jPOGRaptorAI.currentBehaviourStateIndex == (int)JPOGRaptorAI.State.AttackingPlayer)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            // If state switched away, exit safely
            if (jPOGRaptorAI.currentBehaviourStateIndex != (int)JPOGRaptorAI.State.AttackingPlayer)
            {
                LogIfDebugBuild($"Raptor[{raptorId}]: Pounce aborted because state changed");
                ResetPounceFlags();
                yield break;
            }
            // Perform any wrap up: drop body, clean up
            jPOGRaptorAI.DropBodyInMouthServerRpc();

            // Reset pounce flags
            ResetPounceFlags();
        }


        private void ResetPounceFlags()
        {
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
                jPOGRaptorAI.StopCoroutine(pounceCoroutine);
                pounceCoroutine = null;
            }
            IsPouncing = false;
        }

        private void PounceTargetting(PlayerControllerB? targetPlayer)
        {
            if (targetPlayer == null) return;
            //Rigidbody rb = targetPlayer.GetComponent<Rigidbody>();
            //TargetPlayerVelocity = rb.velocity;
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
            if (targetPlayer == null || !IsPouncing) return;
            jPOGRaptorAI.turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
            PounceTargetting(targetPlayer);

            if (PounceAttackDamage)
            {
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: calling pounce RPC");
                jPOGRaptorAI.CheckRaptorPounceHitBoxesClientRPC();
            }
            UpdatePounceTimer();
            if (PounceTimer < PounceDuration)
            {
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: pouncing towards target player");
                jPOGRaptorAI.transform.position += pounceDirection * PounceSpeed * Time.deltaTime;
            }
        }

        // Checks if player are in range for a pounce attack
        public void CheckIfPlayersAreInRangeForPounceAttack()
        {
            if (jPOGRaptorAI.targetPlayer != null)
            {
                float distance = Vector3.Distance(jPOGRaptorAI.transform.position, jPOGRaptorAI.targetPlayer.transform.position);
                if (distance < 10f && HasClearLineToPlayer())
                {
                    if (TimeSincePounceAttack >= 10f)
                    {
                        InRangeForPounceAttack = true;
                        LogIfDebugBuild($"Raptor[{raptorId}]: Player in range and line clear for pounce!");
                    }
                    else
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: target player was in pounce range, but pounce is on cooldown");
                    }
                }
            }
            else
            {
                InRangeForPounceAttack = false;
            }
        }

        /// <summary>
        /// Checks if the direct line to the target player is clear (no walls or obstacles).
        /// </summary>
        private bool HasClearLineToPlayer()
        {
            var player = jPOGRaptorAI.targetPlayer;
            if (player == null) return false;

            Vector3 from = jPOGRaptorAI.transform.position + Vector3.up * 1f;
            Vector3 to = player.transform.position + Vector3.up * 1f;

            bool blocked = Physics.Linecast(from, to, out RaycastHit hit);

            if (blocked)
            {
                // Prevents the raycast from being blocked by the raptor's own colliders
                if (hit.collider.transform.IsChildOf(jPOGRaptorAI.transform))
                {
                    blocked = false;
                }
                else
                {
                    LogIfDebugBuild($"Raptor[{raptorId}]: Line to player blocked by [{hit.collider.name}]");
                }
            }

            return !blocked;
        }

        // Checks the for players in the pounce hitbox
        public void CheckRaptorPounceHitBoxes()
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: PounceAttackClientRPC");

            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity

            Collider[] hitColliders = Physics.OverlapBox(
                jPOGRaptorAI.attackArea.position,
                jPOGRaptorAI.attackArea.localScale,
                Quaternion.identity, playerLayer
                );

            if (hitColliders.Length > 0)
            {
                foreach (var player in hitColliders)
                {
                    PlayerControllerB playerControllerB = jPOGRaptorAI.MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        if (jPOGRaptorAI.CarryingKilledPlayerBody == null)
                        {
                            int playerId = (int)playerControllerB.playerClientId;
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Hit player [{playerId}]");
                            playerControllerB.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Mauling, 0);
                            jPOGRaptorAI.TakeBodyInMouthServerRpc(playerId);
                        }
                        else
                        {
                            playerControllerB.DamagePlayer(40);
                        }
                    }
                }
            }
        }
    }
}
