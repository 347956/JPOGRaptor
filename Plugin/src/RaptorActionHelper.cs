using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace JPOGRaptor.src
{
    internal class RaptorActionHelper
    {

        private readonly JPOGRaptorAI jPOGRaptorAI;
        private Coroutine pounceCoroutine;

        private float maxPounceTime = 6f; // safety timeout for pounce
        private Vector3 pounceDirection;
        public bool isPouncing { get; private set; } = false;
        private int raptorId;
        public bool pounceAttackDamage { get; private set; } = false;
        public float timeSincePounceAttack { get; private set; } = 0f;
        public bool pounceAttackComplete { get; private set; } = false;


        public RaptorActionHelper(JPOGRaptorAI jPOGRaptorAI)
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
            if (isPouncing) return;
            pounceCoroutine = jPOGRaptorAI.StartCoroutine(pounceRoutine());
        }

        private IEnumerator pounceRoutine()
        {
            isPouncing = true;
            pounceAttackDamage = true;
            pounceAttackComplete = false;

            // Predict future player position
            Vector3 predictedPosition = jPOGRaptorAI.targetPlayer != null
            ? jPOGRaptorAI.targetPlayer.transform.position + jPOGRaptorAI.TargetPlayerVelocity * jPOGRaptorAI.pouncePredictionTime
            : jPOGRaptorAI.transform.forward * 5f;

            pounceDirection = (predictedPosition - jPOGRaptorAI.transform.position).normalized;

            // Play pounce animation on all clients
            jPOGRaptorAI.DoAnimationClientRpc("pounceAttack");
            LogIfDebugBuild($"Raptor[{raptorId}]: Pounce started toward {predictedPosition}");

            // Wait for damage window duration
            yield return new WaitForSeconds(2.5f);
            pounceAttackDamage = false;

            // Wait for rest of pounce to complete
            float timer = 0f;
            while (timer < maxPounceTime && jPOGRaptorAI.currentBehaviourStateIndex == (int)jPOGRaptorAI.CurrentState)
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

            // Switch back to chasing
            jPOGRaptorAI.SwitchToBehaviourServerRpc((int)JPOGRaptorAI.State.ChasingPlayer);

            yield break;
        }


        private void ResetPounceFlags()
        {
            isPouncing = false;
            pounceAttackDamage = false;
            pounceAttackComplete = true;
            
            timeSincePounceAttack = 0;
            LogIfDebugBuild($"Raptor[{raptorId}]: Pounce flags reset");
        }
    }
}
