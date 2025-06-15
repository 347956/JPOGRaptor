using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using JPOGRaptor.src;
using Unity.Netcode;
using UnityEngine;

namespace JPOGRaptor {

    // You may be wondering, how does the JPOGRaptor know it is from class JPOGRaptorAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class JPOGRaptorAI : EnemyAI
    {
        public static List<JPOGRaptorAI> AllRaptors = new List<JPOGRaptorAI>();
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        public AudioSource RaptorcallVoice = null!;
        public AudioSource RaptorStepsSFX = null!;
        public Transform MouthBone = null!;
        private State? previousState = null;
        public DeadBodyInfo? CarryingKilledPlayerBody { get; private set; } = null;
#pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        bool isDeadAnimationDone;
        private bool inCallAnimation = false;
        private bool inPounceAttack = false;
        public int raptorId { get; private set; }
        private bool isClimbing;
        private bool isWalking;
        private bool isRunning;

        public RoundManager roundManager { get; private set; }  = null!;
        //public StartOfRound startOfRound { get; private set; } = null!;
        private RaptorPounceHelper raptorPounceHelper = null!;
        private RaptorTargetingHelper raptorTargetingHelper = null!;

        public Vector3 pounceDirection { get; private set; }
        public Vector3 TargetPlayerlastPosition { get; private set; }
        public Vector3 TargetPlayerVelocity { get; private set; }
        public readonly float pouncePredictionTime = 1.5f;
        private bool respondingToHelpCall = false;
        private float lastHeardCallDistanceWhenHeard;

        private Vector3 noisePositionGuess;
        private float noiseApproximation = 14f;
        private float previousAgentSpeed;
        private bool wasOnOffMeshLink;

        public enum State {
            SearchingForPlayer,
            StalkingPlayer,
            ChasingPlayer,
            RespondingToCall,
            AttackingPlayer
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
            assignImportantValues();
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Spawned");
            timeSinceHittingLocalPlayer = 0;
            creatureAnimator.SetTrigger("startWalk");
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }
        private void assignImportantValues()
        {
            AllRaptors.Add(this);
            this.raptorId = AllRaptors.Count - 1;
            //startOfRound = FindObjectOfType<StartOfRound>() ?? throw new Exception("JPOGRaptor: StartOfRound not found!");
            roundManager = FindObjectOfType<RoundManager>() ?? throw new Exception("JPOGRaptor: RoundManager not found!");
            raptorPounceHelper = new RaptorPounceHelper(this); // Initiate the action helper class
            raptorTargetingHelper = new RaptorTargetingHelper(this);
        }


        public override void Update() {
            base.Update();

            if (isEnemyDead) {
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if (!isDeadAnimationDone) {
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                    DoAnimationClientRpc("stopBreath");
                }
                return;
            }
            var state = currentBehaviourStateIndex;
            CheckIfClimbing();
            float speed = agent.velocity.magnitude;
            SetWalkingAnimation(agent.speed);

            if (targetPlayer != null && (state != (int)State.SearchingForPlayer))
            {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            raptorPounceHelper.MoveRaptorDuringPounce(targetPlayer);
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            raptorPounceHelper.UpdateTimeSincePounceAttack();
            // For debugging: logs if the raptors speed gets stuck at 0 despite having a target
            if (agent.speed < 0.1f && targetPlayer != null && currentBehaviourStateIndex != (int)State.AttackingPlayer)
            {
                LogIfDebugBuild($"[WARN] Raptor[{raptorId}] has target but speed is zero! Current state: {(State)currentBehaviourStateIndex}");
            }
        }

        public override void DoAIInterval() {

            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch (currentBehaviourStateIndex) {
                case (int)State.SearchingForPlayer:
                    if (previousBehaviourStateIndex != (int)State.SearchingForPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Entered Searching for player");
                        StateSwitchHelper(State.SearchingForPlayer);
                        respondingToHelpCall = false;
                        //Reset Target Player
                        targetPlayer = null;
                        StartSearch(transform.position);
                    }
                    if (raptorTargetingHelper.FoundClosestPlayerInRange(25f, 5f)) {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Start Target Player");
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.StalkingPlayer);
                    }
                    break;


                case (int)State.StalkingPlayer:
                    if (previousBehaviourStateIndex != (int)State.StalkingPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Entered Stalking Player");
                        CallForHelp();
                        StateSwitchHelper(State.StalkingPlayer);
                        if (targetPlayer != null)
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                        }
                        if (!inCallAnimation)
                        {
                            inCallAnimation = true;
                            StartCoroutine(PlayBark(true));
                        }
                        break;
                    }
                    if (!inCallAnimation)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Stalking Player > finished Call > switching to chasing player");
                        SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        break;
                    }
                    break;

                case (int)State.ChasingPlayer:
                    if (previousBehaviourStateIndex != (int)State.ChasingPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Entered Chasing Player");
                        StopSearch(currentSearch);
                        StateSwitchHelper(State.ChasingPlayer);
                    }

                    // Make sure there is a target
                    if (!raptorTargetingHelper.EnsureTarget())
                    {
                        LogIfDebugBuild($"Raptor[{raptorId}]: No target found, switching to SearchingForPlayer");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        break;
                    }

                    // Set Destination to the target player
                    SetDestinationToPosition(targetPlayer.transform.position);

                    // Check if the player is reachable
                    raptorTargetingHelper.UpdatePathTimeout(targetPlayer);
                    if (!raptorTargetingHelper.CheckIfTargetPlayerIsReachable(targetPlayer))
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Cannot reach the target and timed out");
                        targetPlayer = null;
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        break;
                    }

                    CheckIfPlayersAreInPounceAreaServerRPC();
                    // Switch to pounce behaviour sate if the player is in range for a pounce attack
                    if (raptorPounceHelper.InRangeForPounceAttack)
                    {
                        SwitchToBehaviourServerRpc((int)State.AttackingPlayer);
                        break;
                    }

                    // Check if the player is too far away
                    if (raptorTargetingHelper.IsTargetTooFar())
                    {
                        LogIfDebugBuild($"Raptor[{raptorId}]: Target too far, switching to SearchingForPlayer");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }

                    break;

                case (int)State.RespondingToCall:
                    if (previousBehaviourStateIndex != (int)State.RespondingToCall)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Entered Responding To Call");
                        StateSwitchHelper(State.RespondingToCall);
                    }
                    //If the raptor is responding to a help call and its target was set it needs to move to that player
                    //As the raptor is responding to the help call, we assume it was in help call range to respond and move to the player
                    if (targetPlayer != null && PlayerIsTargetable(targetPlayer, false, false))
                    {
                        //LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Help response. Target player != null, staying on target for");
                        SetDestinationToPosition(targetPlayer.transform.position);
                        if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 15)
                        {
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Help response. Got close enough to the target player, switching to regular chase mode");
                            respondingToHelpCall = false;
                            SwitchToBehaviourServerRpc((int)State.ChasingPlayer);
                        }
                        break;
                    }
                    //Default back to searching for player
                    else
                    {
                        respondingToHelpCall = false;
                        SwitchToBehaviourServerRpc((int)State.SearchingForPlayer);
                    }

                    break;

                case (int)State.AttackingPlayer:
                    if (previousBehaviourStateIndex != (int)State.AttackingPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Entered attacking Player");
                        StateSwitchHelper(State.AttackingPlayer);

                        if (!raptorPounceHelper.IsPouncing)
                        {
                            raptorPounceHelper.StartPounce();
                        }
                        break;
                    }

                    if (raptorPounceHelper.PounceAttackComplete)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Pounce Completed. Checking if switching to chase or search");
                        if (targetPlayer != null)
                        {
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Pounce Completed. Target player != null, continue chasing player");
                            SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        }
                        else
                        {
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Pounce Completed. default, return to searching for player");
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }
                        break;
                    }
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }



        IEnumerator PlayBark(bool shortBark)
        {
            if (shortBark)
            {
                if (respondingToHelpCall)
                {
                    yield return new WaitForSeconds(0.5f);
                }
                DoAnimationClientRpc("barkCall");
                yield return new WaitForSeconds(3f);
                inCallAnimation = false;
            }
            else
            {
                DoAnimationClientRpc("shortBark");
                yield return new WaitForSeconds(1.3f);
            }
            yield break;
        }

        private void StateSwitchHelper(State state)
        {
            if (previousState != state || previousState == null)
            {
                previousBehaviourStateIndex = (int)state;
            }
            SeMovementSpeedPerSate(state);
        }

        private void SeMovementSpeedPerSate(State state)
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Adjusting speed and walking animation for state [{state}]");
            switch (state)
            {

                case State.SearchingForPlayer:
                    agent.speed = 5;
                    break;
                case State.StalkingPlayer:
                    agent.speed = 3;
                    break;
                case State.ChasingPlayer:
                    agent.speed = 8;
                    break;
                case State.RespondingToCall:
                    agent.speed = 10;
                    break;
                case State.AttackingPlayer:
                    agent.speed = 0;
                    break;
            }
        }

        private void SetWalkingAnimation(float agentSpeed)
        {
            if (currentBehaviourStateIndex == (int)State.AttackingPlayer) {
                return;            
            }

            if (Mathf.Abs(previousAgentSpeed - agentSpeed) > 0.3f)
            {
                previousAgentSpeed = agentSpeed;
                if (agentSpeed == 0)
                {
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Stopped Moving T-Pose animation");
                    isWalking = false;
                    isRunning = false;
                    DoAnimationClientRpc("stopMove");
                }
                else if (agentSpeed > 0 && agentSpeed <= 5)
                {
                    isWalking = true;
                    isRunning = false;
                    DoAnimationClientRpc("startWalk");
                }
                else if (agentSpeed > 5)
                {
                    isWalking = false;
                    isRunning = true;
                    DoAnimationClientRpc("startRun");
                }
            }
        }

        public override void OnCollideWithPlayer(Collider other) {
            if (timeSinceHittingLocalPlayer < 1f || inCallAnimation || inPounceAttack)
            {
                LogIfDebugBuild("JPOGRaptor Collision but last time since hitting is too short or in a calling animation. CANT DO DMG");
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                DoAnimationClientRpc("biteAttack");
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(30);
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI? collidedEnemy = null)
        {
            if (collidedEnemy == null || collidedEnemy is JPOGRaptorAI || isEnemyDead || collidedEnemy.isEnemyDead || !collidedEnemy.enemyType.canDie)
            {
                return; //Don't damage other raptors or when dead or to invalid enemies
            }
            LogIfDebugBuild("$JPOGRaptor[{raptorId}]: Collision with other valid enemy!");
            base.OnCollideWithEnemy(other, collidedEnemy);
            DoAnimationClientRpc("biteAttack");
            collidedEnemy.HitEnemy(3, null, true);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
            if(isEnemyDead){
                return;
            }
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            enemyHP -= force;
            if (IsOwner) {
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.
                    raptorPounceHelper.CancelPounce();
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                    return;
                }
            }
            // The Raptor Should target the player who hit them unless they are in the chasing state or attackingplayer state
            // Other states should flow into the chasing state automatically
            // Only during the searching for player state should the raptor quickly switch to chasing when hit
            if (playerWhoHit != null)
            {
                raptorTargetingHelper.TargetPlayerWhoHit(playerWhoHit);
            }

        }


        //The Raptor Should be able to call for help
        public void CallForHelp()
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Raptor calling for help!");
            JPOGRaptorAI[] raptorAIs = UnityEngine.Object.FindObjectsOfType<JPOGRaptorAI>();
            for (int i = 0; i < raptorAIs.Length; i++)
            {
                if(raptorAIs[i] != this && targetPlayer != null)
                {
                    raptorAIs[i].ReactToHelpCall(targetPlayer, base.transform.position, true);
                }
            }
        }

        //The Other Raptors should be able to help
        public void ReactToHelpCall(PlayerControllerB playerToTarget, Vector3 needsHelpRaptorPosition, bool urgent)
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Raptor Reacting to help call!");
            if (!respondingToHelpCall && currentBehaviourStateIndex == (int)State.SearchingForPlayer)
            {
                respondingToHelpCall = true;
                inCallAnimation = true;
                
                //If the call was urgent, set this raptor's target to the target of the raptor calling for help
                if (urgent && targetPlayer != null)
                {
                    StartCoroutine(PlayBark(false));
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Help request was urgent, setting target player");
                    this.targetPlayer = playerToTarget;
                    //Switch to the behaviour so the raptor does not start sprinting immediatly
                    StartCoroutine(ReachtToHelp());
                }
                //Else, just move to the position/direction of raptor calling for help
                else
                {
                    StartCoroutine(PlayBark(true));
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Help request NOT urgent, moving towards call location");
                    lastHeardCallDistanceWhenHeard = Vector3.Distance(base.transform.position, needsHelpRaptorPosition);
                    noisePositionGuess = roundManager.GetRandomNavMeshPositionInRadius(needsHelpRaptorPosition, lastHeardCallDistanceWhenHeard / noiseApproximation);
                    SetDestinationToPosition(noisePositionGuess);
                }
            }
        }

        IEnumerator ReachtToHelp()
        {
            yield return new WaitForSeconds(1.5f);
            SwitchToBehaviourServerRpc((int)State.RespondingToCall);
            yield break;
        }



        public void CheckIfClimbing()
        {
            if (agent.isOnOffMeshLink && !wasOnOffMeshLink)
            {
                // Just started climbing
                wasOnOffMeshLink = true;
                isClimbing = true;
                creatureAnimator.SetBool("isClimbing", isClimbing);
                LogIfDebugBuild($"Raptor[{raptorId}]: Started climbing.");
            }
            else if (!agent.isOnOffMeshLink && wasOnOffMeshLink)
            {
                // Just finished climbing
                wasOnOffMeshLink = false;
                isClimbing = false;
                creatureAnimator.SetBool("isClimbing", isClimbing);
                LogIfDebugBuild($"Raptor[{raptorId}]: Finished climbing.");
                if(isRunning)
                {
                    agent.speed = 8;
                    DoAnimationClientRpc("startRun");
                }
                else if (isWalking)
                {
                    agent.speed = 3;
                    DoAnimationClientRpc("startWalk");
                }
                else
                {
                    agent.speed = 0;
                    DoAnimationClientRpc("startWalk");
                }
                LogIfDebugBuild($"Raptor[{raptorId}]: After CheckIfClimbing: speed=[{agent.speed}], isRunning=[{isRunning}], isWalking=[{isWalking}]");
            }

        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void SetAnimationWalkingSpeedRPC(float speed)
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: setting blend tree moving speed to = [{speed}]");
            creatureAnimator.SetFloat("moveSpeed", speed, 0.1f, Time.deltaTime);
        }

        [ServerRpc]
        public void CheckIfPlayersAreInPounceAreaServerRPC()
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: checking if raptor can pounce");
            CheckIfPlayersAreInPounceAreaClientRPC();                
        }


        [ClientRpc]
        public void CheckIfPlayersAreInPounceAreaClientRPC()
        {
            raptorPounceHelper.CheckIfPlayersAreInRangeForPounceAttack();
        }

        public void SetInCall()
        {
            inCallAnimation = false;
        }

        public void PlayBarkClip(AudioClip audioClip)
        {
            RaptorcallVoice.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(RaptorcallVoice, audioClip);
        }
        public void PlayStepClip(AudioClip audioClip)
        {
            RaptorStepsSFX.PlayOneShot(audioClip);
        }

        public void PlayVoiceClip(AudioClip audioClip)
        {
            creatureVoice.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(creatureVoice, audioClip);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TakeBodyInMouthServerRpc(int killPlayerId)
        {
            TakeBodyInMouthClientRpc(killPlayerId);
        }

        [ClientRpc]
        public void TakeBodyInMouthClientRpc(int killPlayerId)
        {
            TakeBodyInMouth(killPlayerId);
        }

        private void TakeBodyInMouth(int playerId)
        {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Taking boddy of player [{playerId}] in mouth");
            DeadBodyInfo killedPlayerBody = StartOfRound.Instance.allPlayerScripts[playerId].deadBody;
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: deadbody of player cause of death: [{killedPlayerBody.causeOfDeath}]");
            if (killedPlayerBody != null)
            {
                CarryingKilledPlayerBody = killedPlayerBody;
                killedPlayerBody.canBeGrabbedBackByPlayers = false;
                killedPlayerBody.attachedTo = MouthBone;
                killedPlayerBody.attachedLimb = killedPlayerBody.bodyParts[5];
                killedPlayerBody.matchPositionExactly = true;
                killedPlayerBody.MakeCorpseBloody();
            }
        }
        [ServerRpc(RequireOwnership = false)]
        public void DropBodyInMouthServerRpc()
        {
            DropBodyInMouthClientRpc();
        }

        [ClientRpc]
        public void DropBodyInMouthClientRpc()
        {
            DropBodyInMouth();
        }

        private void DropBodyInMouth()
        {
            if (CarryingKilledPlayerBody != null)
            {
                CarryingKilledPlayerBody.speedMultiplier = 3f;
                CarryingKilledPlayerBody.canBeGrabbedBackByPlayers = true;
                CarryingKilledPlayerBody.attachedTo = null;
                CarryingKilledPlayerBody.attachedLimb = null;
                CarryingKilledPlayerBody.matchPositionExactly = false;
                CarryingKilledPlayerBody = null;
            }
        }

        [ClientRpc]
        public void CheckRaptorPounceHitBoxesClientRPC()
        {
            raptorPounceHelper.CheckRaptorPounceHitBoxes();
        }
    }
}