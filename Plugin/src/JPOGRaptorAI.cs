using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using GameNetcodeStuff;
using JPOGRaptor.src;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace JPOGRaptor {

    public class JPOGRaptorAI : EnemyAI
    {
        public static List<JPOGRaptorAI> AllRaptors = [];
        private static int nextRaptorId = 0;
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        public AudioSource RaptorcallVoice = null!;
        public AudioSource RaptorStepsSFX = null!;
        public Transform MouthBone = null!;
        public DeadBodyInfo? CarryingKilledPlayerBody { get; private set; } = null;
#pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSinceHittingOtherEnemy;
        bool isDeadAnimationDone;
        private bool inCallAnimation = false;
        private bool wasStunned = false;
        public int RaptorId { get; private set; }
        private bool isClimbing;
        public AudioClip FootstepClip1 = null!;
        public AudioClip FootstepClip2 = null!;
        private bool isNextStepClip1 = true;
        public NetworkVariable<float> CurrentSpeed = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );


        public RoundManager RoundManager { get; private set; }  = null!;
        //public StartOfRound startOfRound { get; private set; } = null!;
        private RaptorPounceHelper raptorPounceHelper = null!;
        private RaptorTargetingHelper raptorTargetingHelper = null!;

        public string ItemNameToDrop { get; private set; } = "Fancy lamp";
        public int NumberOfItemsToDrop { get; private set; } = 1;

        private bool respondingToHelpCall = false;
        private const float HelpCallRadius = 70f; // only raptors this close to the caller answer a help call

        // Stalking: beyond StalkChargeRange the raptor sneaks between cover points that no player can see, and flees
        // (without a help call) when a player within StalkSeenRange has it in view. Inside StalkChargeRange it stops
        // hiding and charges at the target. Stalking turns into a chase when it takes longer than StalkTimeout.
        private const float StalkChargeRange = 20f;
        private const float StalkSeenRange = 40f;
        private const float StalkSneakSpeed = 4f;
        private const float StalkChargeSpeed = 8f;
        private const float StalkTimeout = 40f;
        private const float CoverNodeSearchRadius = 35f;
        private const float CoverSearchInterval = 1f;
        private float stalkStartTime;
        private bool hasCoverPoint;
        private Vector3 coverPoint;
        private float nextCoverSearchTime;
        // Set before switching to Fleeing when the flee should not call for help (spotted while stalking)
        private bool fleeSilently;
        // The flee that is currently running was started without a help call
        private bool fleeIsSilent;

        // Fleeing after a missed pounce: run to the nearest other raptor and chase again once within FleeRaptorRadius.
        // Without another raptor, run FleeAwayDistance away from the target and stop chasing, unless the player keeps
        // within FleePursuitRadius for FleePursuitTime.
        private const float FleeRaptorRadius = 10f;
        private const float FleeRaptorSearchRadius = 40f; // raptors further away don't count as pack to run to
        private const float FleeToRaptorTimeout = 8f;
        private const float FleeAwayDistance = 50f;
        private const float FleeAwayTimeout = 15f;
        private const float FleePursuitRadius = 15f;
        private const float FleePursuitTime = 5f;
        private static readonly float[] FleeAngles = [0f, 35f, -35f, 70f, -70f, 110f, -110f];
        // No new stalking for this long after a flee ended, which gives the player time to get out of sight
        private const float StalkCooldownAfterFleeing = 5f;
        private float lastFleeTime = float.NegativeInfinity;
        private float fleeStartTime;
        private float lastFleeTick;
        private float pursuedTime;
        private bool hasFleePoint;
        private Vector3 fleePoint;
        private float lastHeardCallDistanceWhenHeard;

        private Vector3 noisePositionGuess;
        private readonly float noiseApproximation = 14f;
        private bool wasOnOffMeshLink;

        public enum State {
            SearchingForPlayer,
            StalkingPlayer,
            ChasingPlayer,
            RespondingToCall,
            AttackingPlayer,
            FleeingFromPlayer
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
            AssignImportantValues();
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Spawned");
            timeSinceHittingLocalPlayer = 0;
            timeSinceHittingOtherEnemy = 0;
            //creatureAnimator.SetTrigger("startWalk");
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            SeMovementSpeedPerSate(State.SearchingForPlayer);
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
        }

        public override void OnDestroy()
        {
            AllRaptors.Remove(this);
            // Release a carried body so it doesn't stay attached to a destroyed mouth bone
            DropBodyInMouth();
            base.OnDestroy();
        }
        private void AssignImportantValues()
        {
            AllRaptors.Add(this);
            this.RaptorId = nextRaptorId++;
            // The prefab may not list every state yet, and EnemyAI indexes this array when switching state
            int stateCount = Enum.GetValues(typeof(State)).Length;
            if (enemyBehaviourStates.Length < stateCount)
            {
                int oldLength = enemyBehaviourStates.Length;
                Array.Resize(ref enemyBehaviourStates, stateCount);
                for (int i = oldLength; i < stateCount; i++)
                {
                    enemyBehaviourStates[i] = new EnemyBehaviourState { name = ((State)i).ToString(), parameterString = "" };
                }
            }
            //startOfRound = FindObjectOfType<StartOfRound>() ?? throw new Exception("JPOGRaptor: StartOfRound not found!");
            RoundManager = FindObjectOfType<RoundManager>() ?? throw new Exception("JPOGRaptor: RoundManager not found!");
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
                    if (IsServer)
                    {
                        DoAnimationClientRpc("stopBreath");
                    }
                    // TODO make the raptor drop a custom scrap item.
/*                    if (IsServer) {
                        DropLoot();
                    }*/
                }
                return;
            }
            var state = currentBehaviourStateIndex;
            if (IsOwner)
            {
                CheckIfClimbing();
            }
            if (IsOwner && !raptorPounceHelper.IsPouncing && !isClimbing)
            {
                // Only write on a meaningful change to avoid syncing a new value every frame
                float velocity = agent.velocity.magnitude;
                if (Mathf.Abs(CurrentSpeed.Value - velocity) > 0.1f)
                {
                    CurrentSpeed.Value = velocity;
                }
            }
            creatureAnimator.SetFloat("moveSpeed", CurrentSpeed.Value);

            if (targetPlayer != null && state != (int)State.SearchingForPlayer && state != (int)State.FleeingFromPlayer)
            {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            }
            raptorPounceHelper.MoveRaptorDuringPounce(targetPlayer);
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
                wasStunned = true;
            }
            else if (wasStunned)
            {
                // Stun is over, restore the speed of the current state
                wasStunned = false;
                SeMovementSpeedPerSate((State)currentBehaviourStateIndex);
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceHittingOtherEnemy += Time.deltaTime;
            raptorPounceHelper.UpdateTimeSincePounceAttack();
            // For debugging: logs if the raptors speed gets stuck at 0 despite having a target
            if (agent.speed < 0.1f && targetPlayer != null && currentBehaviourStateIndex != (int)State.AttackingPlayer)
            {
                LogIfDebugBuild($"[WARN] Raptor[{RaptorId}] has target but speed is zero! Current state: {(State)currentBehaviourStateIndex}");
            }
        }

        private void DropLoot()
        {
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Dropping loot.");

            Item? itemToSpawn = null;
            foreach (Item item in StartOfRound.Instance.allItemsList.itemsList)
            {
                if (item.itemName == ItemNameToDrop)
                {
                    itemToSpawn = item;
                    break;
                }
            }

            if (itemToSpawn == null)
            {
                Plugin.Logger.LogError($"JPOGRaptor[{RaptorId}]: Item to drop '{ItemNameToDrop}' not found in allItemsList!");
                return;
            }

            for( int i = 0; i < NumberOfItemsToDrop; i++)
            {
                Vector3 dropPosition = transform.position + UnityEngine.Random.insideUnitSphere * 1f;
                dropPosition.y = transform.position.y + 0.5f;

                GameObject? droppedScrap = Instantiate(itemToSpawn.spawnPrefab, dropPosition, Quaternion.identity);
                if (droppedScrap.TryGetComponent(out GrabbableObject grabbableObject))
                {
                    grabbableObject.fallTime = 0.5f;
                    grabbableObject.NetworkObject.Spawn(destroyWithScene: true);
                    int scrapValue = UnityEngine.Random.Range(20, 100);
                    grabbableObject.scrapValue = scrapValue;
                    Plugin.Logger.LogInfo($"JPOGRaptor[{RaptorId}]: Dropped '{itemToSpawn.name}' with NetworkObjectId: {grabbableObject.NetworkObjectId}");
                    Plugin.Logger.LogInfo($"JPOGRaptor[{RaptorId}]: Set scrapValue to: {grabbableObject.scrapValue}");
                }
                else
                {
                    Plugin.Logger.LogWarning($"JPOGRaptor[{RaptorId}]: Spawned item '{itemToSpawn.name}' does not have a GrabbableObject component!");
                    return;
                }
            }
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Successfully dropped {NumberOfItemsToDrop} of '{itemToSpawn.itemName}'.");
        }

        public override void DoAIInterval() {

            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) {
                return;
            };
            raptorTargetingHelper.HandleTargetUnreachability();
            switch (currentBehaviourStateIndex) {
                case (int)State.SearchingForPlayer:
                    if (previousBehaviourStateIndex != (int)State.SearchingForPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Entered Searching for player");
                        StateSwitchHelper(State.SearchingForPlayer);
                        respondingToHelpCall = false;
                        targetPlayer = null;
                        StartSearch(transform.position);
                    }
                    // While answering a help call the target was set by the caller, don't overwrite it
                    if (!respondingToHelpCall)
                    {
                        // Right after fleeing the raptor doesn't start stalking again, only a player that gets too close is targeted
                        bool stalkOnCooldown = RaptorRules.IsStalkOnCooldown(Time.time, lastFleeTime, StalkCooldownAfterFleeing);
                        switch (raptorTargetingHelper.TryAcquireTarget(!stalkOnCooldown))
                        {
                            case RaptorTargetingHelper.AcquireResult.TooClose:
                                LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Player too close");
                                if (!TryInstantPounce())
                                {
                                    StopSearch(currentSearch);
                                    SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                                }
                                break;
                            case RaptorTargetingHelper.AcquireResult.Spotted:
                                LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Spotted a lone player");
                                StopSearch(currentSearch);
                                SwitchToBehaviourClientRpc((int)State.StalkingPlayer);
                                break;
                        }
                    }
                    break;


                case (int)State.StalkingPlayer:
                    if (previousBehaviourStateIndex != (int)State.StalkingPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Entered Stalking Player");
                        StateSwitchHelper(State.StalkingPlayer);
                        stalkStartTime = Time.time;
                        hasCoverPoint = false;
                        nextCoverSearchTime = 0f;
                    }

                    // Target gone, dead or no longer reachable
                    if (targetPlayer == null || !PlayerIsTargetable(targetPlayer, false, false)
                        || !raptorTargetingHelper.CheckTargetPathReachability(targetPlayer))
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Stalking > target lost, switching to SearchingForPlayer");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        break;
                    }

                    // Creep up on the target and pounce as soon as it is in range
                    if (TryInstantPounce())
                    {
                        break;
                    }

                    // Another player joined the target before the pounce: call for help and chase normally
                    if (!raptorTargetingHelper.IsAlone(targetPlayer))
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Stalking > target is no longer alone, calling for help and chasing");
                        CallForHelp();
                        SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        break;
                    }

                    if (Time.time - stalkStartTime > StalkTimeout)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Stalking > took too long, switching to chasing player");
                        SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        break;
                    }

                    float distanceToTarget = Vector3.Distance(transform.position, targetPlayer.transform.position);
                    if (distanceToTarget <= StalkChargeRange)
                    {
                        // Close enough: stop hiding and charge. The pounce check above fires once in pounce range
                        if (distanceToTarget < RaptorPounceHelper.PounceRange)
                        {
                            // In range but the pounce is on cooldown or blocked
                            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Stalking > in range but cannot pounce, switching to chasing player");
                            SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                            break;
                        }
                        agent.speed = StalkChargeSpeed;
                        SetDestinationToPosition(targetPlayer.transform.position);
                        break;
                    }

                    // Spotted while sneaking in: flee without calling for help (a possible pounce was tried above)
                    if (raptorTargetingHelper.IsSeenByAnyPlayer(StalkSeenRange))
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Stalking > spotted by a player, fleeing");
                        fleeSilently = true;
                        SwitchToBehaviourClientRpc((int)State.FleeingFromPlayer);
                        break;
                    }

                    agent.speed = StalkSneakSpeed;
                    if (hasCoverPoint && !raptorTargetingHelper.IsHiddenFromAllPlayers(coverPoint, StalkSeenRange))
                    {
                        // A player moved and can see the cover point now
                        hasCoverPoint = false;
                        nextCoverSearchTime = 0f;
                    }
                    if ((!hasCoverPoint || Vector3.Distance(transform.position, coverPoint) < 2f) && Time.time >= nextCoverSearchTime)
                    {
                        nextCoverSearchTime = Time.time + CoverSearchInterval;
                        hasCoverPoint = TryPickCoverPoint(out coverPoint);
                    }
                    // Without cover, advance straight at the target and accept the risk of being spotted
                    SetDestinationToPosition(hasCoverPoint ? coverPoint : targetPlayer.transform.position);
                    break;

                case (int)State.ChasingPlayer:
                    if (previousBehaviourStateIndex != (int)State.ChasingPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Entered Chasing Player");
                        StopSearch(currentSearch);
                        StateSwitchHelper(State.ChasingPlayer);
                    }

                    // Make sure there is a target
                    if (!raptorTargetingHelper.EnsureTarget())
                    {
                        LogIfDebugBuild($"Raptor[{RaptorId}]: No target found, switching to SearchingForPlayer");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        break;
                    }

                    // Set Destination to the target player
                    SetDestinationToPosition(targetPlayer.transform.position);

                    // Check if the player is reachable
                    raptorTargetingHelper.UpdatePathTimeout(targetPlayer);
                    if (!raptorTargetingHelper.CheckIfTargetPlayerIsReachable())
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Cannot reach the target and timed out, looking for another target");
                        raptorTargetingHelper.RetargetOrSearch();
                        break;
                    }

                    // Only the owner reads the result, so no RPC round trip is needed
                    raptorPounceHelper.CheckIfPlayersAreInRangeForPounceAttack();
                    // Switch to pounce behaviour sate if the player is in range for a pounce attack
                    if (raptorPounceHelper.InRangeForPounceAttack)
                    {
                        SwitchToBehaviourServerRpc((int)State.AttackingPlayer);
                        break;
                    }

                    // A target that is too far away is handled by HandleTargetUnreachability at the start of the interval
                    break;

                case (int)State.RespondingToCall:
                    if (previousBehaviourStateIndex != (int)State.RespondingToCall)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Entered Responding To Call");
                        StateSwitchHelper(State.RespondingToCall);
                    }
                    //If the raptor is responding to a help call and its target was set it needs to move to that player
                    //As the raptor is responding to the help call, we assume it was in help call range to respond and move to the player
                    if (targetPlayer != null && PlayerIsTargetable(targetPlayer, false, false))
                    {
                        //LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Help response. Target player != null, staying on target for");
                        SetDestinationToPosition(targetPlayer.transform.position);
                        if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 15)
                        {
                            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Help response. Got close enough to the target player, switching to regular chase mode");
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
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Entered attacking Player");
                        StateSwitchHelper(State.AttackingPlayer);

                        if (!raptorPounceHelper.IsPouncing)
                        {
                            raptorPounceHelper.StartPounce();
                        }
                        break;
                    }

                    if (raptorPounceHelper.PounceAttackComplete)
                    {
                        bool pounceMissed = targetPlayer != null && targetPlayer.isPlayerControlled
                            && !targetPlayer.isPlayerDead && CarryingKilledPlayerBody == null;
                        if (pounceMissed)
                        {
                            // A raptor right next to this one is the pack: no reason to flee, keep chasing without calling
                            JPOGRaptorAI? packMate = FindRaptorToFleeTo();
                            if (packMate != null && RaptorRules.IsWithinRadius(Vector3.Distance(transform.position, packMate.transform.position), FleeRaptorRadius))
                            {
                                LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Pounce missed, but raptor [{packMate.RaptorId}] is close, chasing again");
                                agent.speed = 8f;
                                SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                                break;
                            }
                            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Pounce missed, fleeing");
                            fleeSilently = false;
                            SwitchToBehaviourClientRpc((int)State.FleeingFromPlayer);
                            break;
                        }
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Pounce Completed. Checking if switching to chase or search");
                        if (targetPlayer != null)
                        {
                            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Pounce Completed. Target player != null, continue chasing player");
                            agent.speed = 8f;
                            SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        }
                        else
                        {
                            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Pounce Completed. default, return to searching for player");
                            agent.speed = 5f;
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }
                        break;
                    }
                    break;

                case (int)State.FleeingFromPlayer:
                    if (previousBehaviourStateIndex != (int)State.FleeingFromPlayer)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Entered Fleeing");
                        StateSwitchHelper(State.FleeingFromPlayer);
                        fleeStartTime = Time.time;
                        lastFleeTick = Time.time;
                        pursuedTime = 0f;
                        hasFleePoint = false;
                        // Always call, even when no other raptor is alive, so players can't tell whether help is coming
                        // Being spotted while stalking is the exception: that flee stays quiet
                        fleeIsSilent = fleeSilently;
                        fleeSilently = false;
                        if (!fleeIsSilent)
                        {
                            CallForHelp();
                            StartCoroutine(PlayBark(false));
                        }
                    }

                    if (targetPlayer == null || !PlayerIsTargetable(targetPlayer, false, false))
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Fleeing > target lost, switching to SearchingForPlayer");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        break;
                    }

                    float fleeDelta = Time.time - lastFleeTick;
                    lastFleeTick = Time.time;
                    // Updated every interval while fleeing, so it holds the moment the flee ended
                    lastFleeTime = Time.time;

                    JPOGRaptorAI? raptorToFleeTo = FindRaptorToFleeTo();
                    if (raptorToFleeTo != null)
                    {
                        if (RaptorRules.IsWithinRadius(Vector3.Distance(transform.position, raptorToFleeTo.transform.position), FleeRaptorRadius)
                            || Time.time - fleeStartTime > FleeToRaptorTimeout)
                        {
                            // After being spotted while stalking the raptor starts over instead of chasing
                            State nextState = RaptorRules.ChasesAfterFlee(fleeIsSilent) ? State.ChasingPlayer : State.SearchingForPlayer;
                            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Fleeing > reached another raptor (or timed out), switching to {nextState}");
                            SwitchToBehaviourClientRpc((int)nextState);
                            break;
                        }
                        SetDestinationToPosition(raptorToFleeTo.transform.position);
                        break;
                    }

                    // No other raptor to run to: run away from the target instead
                    if (!hasFleePoint)
                    {
                        hasFleePoint = TryFindFleePoint(out fleePoint);
                        if (!hasFleePoint)
                        {
                            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Fleeing > nowhere to run, chasing again");
                            SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                            break;
                        }
                    }

                    // A player that keeps following the raptor gets pounced on, or chased again
                    bool pursued = RaptorRules.IsWithinRadius(Vector3.Distance(transform.position, targetPlayer.transform.position), FleePursuitRadius);
                    pursuedTime = RaptorRules.UpdatePursuedTime(pursuedTime, pursued, fleeDelta);
                    if (pursuedTime > FleePursuitTime)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Fleeing > player keeps following, fighting back");
                        if (!TryInstantPounce())
                        {
                            SwitchToBehaviourClientRpc((int)State.ChasingPlayer);
                        }
                        break;
                    }

                    if (Vector3.Distance(transform.position, fleePoint) < 3f || Time.time - fleeStartTime > FleeAwayTimeout)
                    {
                        LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Fleeing > got away, giving up the chase");
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }
                    break;

                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }



        /// <summary>
        /// Starts a pounce right away when the target is in range, in line of sight and the pounce is off cooldown.
        /// Returns false when no pounce was started so the caller can fall back to another behaviour.
        /// </summary>
        public bool TryInstantPounce()
        {
            if (isEnemyDead || currentBehaviourStateIndex == (int)State.AttackingPlayer || raptorPounceHelper.IsPouncing)
            {
                return false;
            }
            raptorPounceHelper.CheckIfPlayersAreInRangeForPounceAttack();
            if (!raptorPounceHelper.InRangeForPounceAttack)
            {
                return false;
            }
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Instant pounce");
            StopSearch(currentSearch);
            respondingToHelpCall = false;
            SwitchToBehaviourServerRpc((int)State.AttackingPlayer);
            return true;
        }

        // Picks the closest outside AI node to the target that no player can see and that is closer to the target than
        // the raptor is, and sets it as destination
        private bool TryPickCoverPoint(out Vector3 point)
        {
            Vector3 targetPosition = targetPlayer.transform.position;
            float currentDistance = Vector3.Distance(transform.position, targetPosition);
            List<(Vector3 position, float distance)> candidates = [];
            foreach (GameObject node in allAINodes)
            {
                if (node == null) continue;
                Vector3 position = node.transform.position;
                if (Vector3.Distance(transform.position, position) > CoverNodeSearchRadius) continue;
                float distance = Vector3.Distance(position, targetPosition);
                // Every hop has to get closer to the target
                if (distance >= currentDistance - 3f) continue;
                if (!raptorTargetingHelper.IsHiddenFromAllPlayers(position, StalkSeenRange)) continue;
                candidates.Add((position, distance));
            }
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            // Only the best few get the (more expensive) path check
            for (int i = 0; i < candidates.Count && i < 5; i++)
            {
                if (SetDestinationToPosition(candidates[i].position, true))
                {
                    point = candidates[i].position;
                    return true;
                }
            }
            point = default;
            return false;
        }

        // The nearest other raptor within FleeRaptorSearchRadius that is alive and not fleeing itself
        private JPOGRaptorAI? FindRaptorToFleeTo()
        {
            JPOGRaptorAI? nearest = null;
            float nearestDistance = FleeRaptorSearchRadius;
            foreach (JPOGRaptorAI other in AllRaptors)
            {
                if (other == null || other == this || other.isEnemyDead || other.currentBehaviourStateIndex == (int)State.FleeingFromPlayer) continue;
                float distance = Vector3.Distance(transform.position, other.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = other;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        // Finds a reachable spot about FleeAwayDistance away from the target and sets it as destination
        private bool TryFindFleePoint(out Vector3 point)
        {
            Vector3 away = transform.position - targetPlayer.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude < 0.01f ? -transform.forward : away.normalized;

            foreach (float angle in FleeAngles)
            {
                Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * away * FleeAwayDistance;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 15f, agent.areaMask)
                    && Vector3.Distance(hit.position, transform.position) > FleeAwayDistance * 0.6f
                    && SetDestinationToPosition(hit.position, true))
                {
                    point = hit.position;
                    return true;
                }
            }
            point = default;
            return false;
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
                inCallAnimation = false;
            }
            yield break;
        }

        private void StateSwitchHelper(State state)
        {
            // Marks the state switch as handled, so the entry block of the state only runs once
            previousBehaviourStateIndex = (int)state;
            SeMovementSpeedPerSate(state);
        }

        private void SeMovementSpeedPerSate(State state)
        {
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Adjusting speed for state [{state}]");
            switch (state)
            {

                case State.SearchingForPlayer:
                    agent.speed = 5;
                    break;
                case State.StalkingPlayer:
                    agent.speed = StalkSneakSpeed;
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
                case State.FleeingFromPlayer:
                    agent.speed = 10;
                    break;
            }
        }

        public override void OnCollideWithPlayer(Collider other) {
            if (timeSinceHittingLocalPlayer < 1.5f || inCallAnimation || raptorPounceHelper.IsPouncing)
            {
                LogIfDebugBuild("JPOGRaptor Collision but last time since hitting is too short or in a calling animation. CANT DO DMG");
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(30);
                // This runs on the victim's client, which can't invoke ClientRpcs itself
                BiteAttackServerRpc(playerControllerB.NetworkObjectId);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void BiteAttackServerRpc(ulong playerNetworkObjectId)
        {
            DoAnimationClientRpc("biteAttack");
            SpawnPlayerBloodOnClientRpc(playerNetworkObjectId, Vector3.zero);
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI? collidedEnemy = null)
        {
            if (!IsOwner || timeSinceHittingOtherEnemy < 5f) return;
            if (collidedEnemy == null || collidedEnemy is JPOGRaptorAI || isEnemyDead || collidedEnemy.isEnemyDead || !collidedEnemy.enemyType.canDie)
            {
                return; //Don't damage other raptors or when dead or to invalid enemies
            }
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Collision with other valid enemy!");
            base.OnCollideWithEnemy(other, collidedEnemy);
            DoAnimationClientRpc("biteAttack");
            collidedEnemy.HitEnemy(3, null, true);
            timeSinceHittingOtherEnemy = 0f;
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
                    DropBodyInMouthServerRpc();
                    // We need to stop our search coroutine, because the game does not do that by default.
                    if (searchCoroutine != null)
                    {
                        StopCoroutine(searchCoroutine);
                        searchCoroutine = null;
                    }
                    KillEnemyOnOwnerClient();
                    return;
                }
            }
            // The Raptor Should target the player who hit them unless they are in the chasing state or attackingplayer state
            // Other states should flow into the chasing state automatically
            // Only during the searching for player state should the raptor quickly switch to chasing when hit
            // HitEnemy runs on every client, only the owner decides the behaviour
            if (IsOwner && playerWhoHit != null)
            {
                raptorTargetingHelper.TargetPlayerWhoHit(playerWhoHit);
            }

        }


        //The Raptor Should be able to call for help
        public void CallForHelp()
        {
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Raptor calling for help!");
            if (targetPlayer == null) return;
            foreach (JPOGRaptorAI otherRaptor in AllRaptors)
            {
                if (otherRaptor != null && otherRaptor != this && !otherRaptor.isEnemyDead
                    && RaptorRules.IsWithinRadius(Vector3.Distance(transform.position, otherRaptor.transform.position), HelpCallRadius))
                {
                    otherRaptor.ReactToHelpCall(targetPlayer, base.transform.position, true);
                }
            }
        }

        //The Other Raptors should be able to help
        public void ReactToHelpCall(PlayerControllerB playerToTarget, Vector3 needsHelpRaptorPosition, bool urgent)
        {
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Raptor Reacting to help call!");
            // previousBehaviourStateIndex is only Searching once the entry block ran, which would otherwise wipe the target set here
            if (!respondingToHelpCall && currentBehaviourStateIndex == (int)State.SearchingForPlayer
                && previousBehaviourStateIndex == (int)State.SearchingForPlayer)
            {
                respondingToHelpCall = true;
                inCallAnimation = true;
                
                //If the call was urgent, set this raptor's target to the target of the raptor calling for help
                if (urgent && playerToTarget != null)
                {
                    StartCoroutine(PlayBark(false));
                    LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Help request was urgent, setting target player");
                    this.targetPlayer = playerToTarget;
                    //Switch to the behaviour so the raptor does not start sprinting immediatly
                    StartCoroutine(ReachtToHelp());
                }
                //Else, just move to the position/direction of raptor calling for help
                else
                {
                    StartCoroutine(PlayBark(true));
                    LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Help request NOT urgent, moving towards call location");
                    lastHeardCallDistanceWhenHeard = Vector3.Distance(base.transform.position, needsHelpRaptorPosition);
                    noisePositionGuess = RoundManager.GetRandomNavMeshPositionInRadius(needsHelpRaptorPosition, lastHeardCallDistanceWhenHeard / noiseApproximation);
                    SetDestinationToPosition(noisePositionGuess);
                }
            }
        }

        IEnumerator ReachtToHelp()
        {
            yield return new WaitForSeconds(1.5f);
            if (isEnemyDead || currentBehaviourStateIndex != (int)State.SearchingForPlayer)
            {
                respondingToHelpCall = false;
                inCallAnimation = false;
                yield break;
            }
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
                SetAnimationBoolClientRPC("isClimbing", isClimbing);
                LogIfDebugBuild($"Raptor[{RaptorId}]: Started climbing.");
            }
            else if (!agent.isOnOffMeshLink && wasOnOffMeshLink)
            {
                // Just finished climbing
                wasOnOffMeshLink = false;
                isClimbing = false;
                SetAnimationBoolClientRPC("isClimbing", isClimbing);
                LogIfDebugBuild($"Raptor[{RaptorId}]: Finished climbing.");
            }
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void SetAnimationWalkingSpeedClientRPC()
        {
            float speed = agent.velocity.magnitude;
            //LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: setting blend tree moving speed to = [{speed}]");
            creatureAnimator.SetFloat("moveSpeed", speed, 0.1f, Time.deltaTime);
        }
        [ClientRpc]
        public void SetAnimationBoolClientRPC(string trigger, bool value)
        {
            creatureAnimator.SetBool(trigger, value);
        }

        public void PlayBarkClip(AudioClip audioClip)
        {
            RaptorcallVoice.PlayOneShot(audioClip);
            WalkieTalkie.TransmitOneShotAudio(RaptorcallVoice, audioClip);
        }

        public void PlayStepClip()
        {
            AudioClip stepClip;
            if (isNextStepClip1)
            {
                stepClip = FootstepClip1;
            }
            else
            {
                stepClip = FootstepClip2;
            }

            RaptorStepsSFX.PlayOneShot(stepClip);

            isNextStepClip1 = !isNextStepClip1;
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
            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Taking boddy of player [{playerId}] in mouth");
            StartCoroutine(TakeBodyInMouthWhenReady(playerId));
        }

        // On remote clients the dead body may not be spawned yet when the RPC arrives, so wait for it
        private IEnumerator TakeBodyInMouthWhenReady(int playerId)
        {
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[playerId];
            float timeout = 2f;
            while (player.deadBody == null && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            DeadBodyInfo? killedPlayerBody = player.deadBody;
            if (killedPlayerBody == null)
            {
                Plugin.Logger.LogWarning($"JPOGRaptor[{RaptorId}]: deadbody of player [{playerId}] never appeared, cannot carry it.");
                yield break;
            }

            LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: deadbody of player cause of death: [{killedPlayerBody.causeOfDeath}]");
            CarryingKilledPlayerBody = killedPlayerBody;
            killedPlayerBody.canBeGrabbedBackByPlayers = false;
            killedPlayerBody.attachedTo = MouthBone;
            killedPlayerBody.attachedLimb = killedPlayerBody.bodyParts[5];
            killedPlayerBody.matchPositionExactly = true;
            killedPlayerBody.MakeCorpseBloody();
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
        public void BeginPounceClientRpc(int targetPlayerId)
        {
            raptorPounceHelper.BeginPounce(targetPlayerId);
        }

        [ClientRpc]
        public void CheckRaptorPounceHitBoxesClientRPC()
        {
            raptorPounceHelper.CheckRaptorPounceHitBoxes();
        }
        [ClientRpc]
        public void SpawnPlayerBloodOnClientRpc(ulong playerNetworkObjectId, Vector3 bloodDirection)
        {
            // This ClientRpc will execute on all clients.
            // Each client will try to find the player by its NetworkObjectId.
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject networkObject))
            {
                Plugin.Logger.LogWarning($"JPOGRaptor[{RaptorId}]: Could not find networked player object with ID {playerNetworkObjectId} to spawn blood.");
                return;
            }

            if (networkObject.TryGetComponent(out PlayerControllerB player))
            {
                // Call the player's native DropBlood method
                player.DropBlood(bloodDirection, leaveBlood: true, leaveFootprint: false);
                LogIfDebugBuild($"JPOGRaptor[{RaptorId}]: Triggered DropBlood for player {player.playerUsername} at {player.transform.position}.");
            }
            else
            {
                Plugin.Logger.LogWarning($"JPOGRaptor[{RaptorId}]: Found NetworkObject with ID {playerNetworkObjectId} but no PlayerControllerB component for blood.");
            }
        }
    }
}