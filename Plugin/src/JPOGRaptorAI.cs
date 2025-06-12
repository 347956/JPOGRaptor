using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
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
        private State ?previousState = null;
        private DeadBodyInfo? CarryingKilledPlayerBody = null;
        #pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSincePounceAttack;
        bool isDeadAnimationDone;
        private bool inCallAnimation = false;
        private bool isRunning;
        private bool inPounceAttack = false;
        private bool pounceAttackDamage;
        private bool inRangeForPounceAttack;
        private bool pounceAttackComplete;
        private int raptorId = 99999;

        private RoundManager? roundManager;


        private float pounceTimer = 0f;
        private float pounceDuration = 1f;
        //private float pounceDistance = 10f;
        private float pounceSpeed = 15f;
        private Vector3 pounceDirection;
        private Vector3 TargetPlayerlastPosition;
        private Vector3 TargetPlayerVelocity;
        private float pouncePredictionTime = 1.5f;
        private bool respondingToHelpCall = false;
        private float lastHeardCallDistanceWhenHeard;

        private Vector3 noisePositionGuess;
        private float noiseApproximation = 14f;

        enum State {
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
            AllRaptors.Add(this);
            this.raptorId = AllRaptors.Count - 1;
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Spawned");
            timeSinceHittingLocalPlayer = 0;
            timeSincePounceAttack = 10;
            creatureAnimator.SetTrigger("startWalk");
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }

        public override void Update() {
            base.Update();
            if(isEnemyDead){
                // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
                if(!isDeadAnimationDone){ 
                    LogIfDebugBuild("Stopping enemy voice with janky code.");
                    isDeadAnimationDone = true;
                    creatureVoice.Stop();
                    creatureVoice.PlayOneShot(dieSFX);
                }
                return;
            }
            var state = currentBehaviourStateIndex;
            if (targetPlayer != null && (state != (int)State.SearchingForPlayer))
            {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            if (targetPlayer != null && inPounceAttack)
            {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                if (pounceAttackDamage)
                {
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: calling pounce RPC");
                    PounceAttackClientRpc();
                }
                pounceTimer += Time.deltaTime;
                if(pounceTimer < pounceDuration)
                {
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: pouncing towards target player");
                    transform.position += pounceDirection * pounceSpeed * Time.deltaTime;
                }
                return;
            }
            if (targetPlayer != null)
            {
                //Rigidbody rb = targetPlayer.GetComponent<Rigidbody>();
                //TargetPlayerVelocity = rb.velocity;
                TargetPlayerVelocity = (targetPlayer.transform.position - TargetPlayerlastPosition) / Time.deltaTime;
                TargetPlayerlastPosition = targetPlayer.transform.position;
            }
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSincePounceAttack += Time.deltaTime;
        }

        public override void DoAIInterval() {
            
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };

            switch(currentBehaviourStateIndex) {
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
                    if (FoundClosestPlayerInRange(25f, 3f)){
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Start Target Player");
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.StalkingPlayer);
                    }
                    break;


                case (int)State.StalkingPlayer:
                    if(previousBehaviourStateIndex != (int)State.StalkingPlayer)
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
                    if(previousBehaviourStateIndex != (int)State.ChasingPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Entered Chasing Player");
                        StopSearch(currentSearch);
                        StateSwitchHelper(State.ChasingPlayer);
                    }
                    if (targetPlayer != null)
                    {
                        //LogIfDebugBuild($"JPOGRaptor[{raptorId}]: target player != null, staying on target");
                        SetDestinationToPosition(targetPlayer.transform.position);
                        if (inRangeForPounceAttack)
                        {
                            inRangeForPounceAttack = false;
                            SwitchToBehaviourServerRpc((int)State.AttackingPlayer);
                            break;
                        }
                        CheckIfPlayersAreInPounceAreaServerRPC();
                        if (Vector3.Distance(transform.position, targetPlayer.transform.position) > 30)
                        {
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Player too far away, stopping chase.");
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }
                        break;
                    }
                    if (targetPlayer == null)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: entered chasing target state, but the target = null. Attempting to set new target");
                        if (TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) < 20 && CheckLineOfSightForPosition(targetPlayer.transform.position)))
                        {
                            if(targetPlayer != null)
                            {
                                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: new target set");
                                SetDestinationToPosition(targetPlayer.transform.position);
                                break;
                            }
                            else
                            {
                                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Could not set target");
                                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                                break;
                            }
                        }
                        else
                        {
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: (Default) Could not set target");
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            break;
                        }
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
                    if (targetPlayer != null && PlayerIsTargetable(targetPlayer,false, false))
                    {
                        //LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Help response. Target player != null, staying on target for");
                        SetDestinationToPosition(targetPlayer.transform.position);
                        if(Vector3.Distance(transform.position, targetPlayer.transform.position) < 15)
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
                    if(previousBehaviourStateIndex != (int)State.AttackingPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Entered attacking Player");
                        StateSwitchHelper(State.AttackingPlayer);

                        if (!inPounceAttack)
                        {
                            pounceAttackComplete = false;
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Beginning Pounce Coroutine");
                            StartCoroutine(PounceAttack());
                        }
                        break;
                    }

                    if (pounceAttackComplete)
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

        bool FoundClosestPlayerInRange(float range, float senseRange) {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if(targetPlayer == null){
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }
        
        bool TargetClosestPlayerInAnyCase() {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }
            if(targetPlayer == null) return false;
            return true;
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


        IEnumerator PounceAttack() {
            inPounceAttack = true;
            pounceAttackDamage = true;
            Vector3 predictedPosition = targetPlayer.transform.position + TargetPlayerVelocity * pouncePredictionTime;
            pounceDirection = (predictedPosition - transform.position).normalized;
            //pounceSpeed = pounceDistance / pounceDuration;
            pounceTimer = 0f;
            DoAnimationClientRpc("pounceAttack");
            yield return new WaitForSeconds(2.5f);
            pounceAttackDamage = false;
            agent.speed = 0;
            yield return new WaitForSeconds(3f);
            // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
            if (currentBehaviourStateIndex != (int)State.AttackingPlayer)
            {
                yield break;
            }
            DropBodyInMouth();
            timeSincePounceAttack = 0;
            inPounceAttack = false;
            SwitchToBehaviourServerRpc((int)State.ChasingPlayer);
        }



        private void StateSwitchHelper(State state)
        {
            if(previousState != state || previousState == null)
            {
                SetWallkingAnimationPerSate(state);
                previousBehaviourStateIndex = (int)state;
            }
        }

        private void SetWallkingAnimationPerSate(State state)
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
                    isRunning = true;
                    agent.speed = 8;
                    break;
                case State.RespondingToCall:
                    isRunning = true;
                    agent.speed = 10;
                    break;
                case State.AttackingPlayer:
                    agent.speed = 0;
                    break;
            }
            SetWalkingAnimation(agent.speed);
        }

        private void SetWalkingAnimation(float agentSpeed)
        {
            if (agentSpeed == 0)
            {
                if (isRunning)
                {
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Stopping running animation");
                    isRunning = false;
                    DoAnimationClientRpc("stopRun");
                }
                else
                {
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Stopping walking animation");
                    DoAnimationClientRpc("stopWalk");
                }

            }
            else if (agentSpeed > 0 && agentSpeed <= 5)
            {
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Beginning walking animation");
                DoAnimationClientRpc("startWalk");
            }
            else if (agentSpeed > 5)
            {
                LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Beginning running animation");
                DoAnimationClientRpc("startRun");
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
                LogIfDebugBuild("JPOGRaptor Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(30);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if(isEnemyDead){
                return;
            }
            enemyHP -= force;
            if (IsOwner) {
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.

                    StopCoroutine(PounceAttack());
                    // We need to stop our search coroutine, because the game does not do that by default.
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
            }
        }

        public bool PlayerHasHorizontalLOS(PlayerControllerB player)
        {
            Vector3 to = base.transform.position - player.transform.position;
            to.y = 0f;
            return Vector3.Angle(player.transform.forward, to) < 68f;
        }

        //Helper method to set the target to null and other targetting related properties
        public void ResetTarget()
        {

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


        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void PounceAttackClientRpc() {
            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: PounceAttackClientRPC");
            int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
            Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
            if(hitColliders.Length > 0){
                foreach (var player in hitColliders){
                    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
                    if (playerControllerB != null)
                    {
                        if(CarryingKilledPlayerBody == null)
                        {
                            int playerId = (int)playerControllerB.playerClientId;
                            LogIfDebugBuild($"JPOGRaptor[{raptorId}]: Hit player [{playerId}]");
                            playerControllerB.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Mauling, 0);
                            TakeBodyInMouthServerRpc(playerId);
                        }
                        else
                        {
                            playerControllerB.DamagePlayer(40);
                        }
                    }
                }
            }
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
            CheckIfPlayersAreInPounceArea();
        }


        public void CheckIfPlayersAreInPounceArea()
        {
            if (targetPlayer != null)
            {
                if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 10)
                {
                    LogIfDebugBuild($"JPOGRaptor[{raptorId}]: target Player in range for pounce attack!");
                    if (timeSincePounceAttack >= 10f)
                    {
                        inRangeForPounceAttack = true;
                    }
                    else
                    {
                        LogIfDebugBuild($"JPOGRaptor[{raptorId}]: target player was in pounce range, but pounce is on cooldown");
                    }
                }
            }
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
    }
}