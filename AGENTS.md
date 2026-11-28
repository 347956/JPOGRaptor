# JPOGRaptor: guide for agents

A Lethal Company mod that adds the Raptor from Jurassic Park Operation Genesis as an outside enemy. It's a BepInEx plugin that uses DawnLib/Dusk for registration, Unity Netcode for GameObjects for multiplayer, and Unity asset bundles for the content.

`README.md` is the user-facing reference for building, CI and releasing. This file maps the code and lists the things that are easy to break.

## How the pieces fit

This repo is **a folder inside a host Unity project** (`<host>/Assets/LethalCompany/Mods/plugins/JPOGRaptor/`), not a standalone Unity project. The host supplies the game scripts (Assembly-CSharp), the Unity packages and the DawnLib DLL. Unity ignores every folder ending in `~`, so the C# plugin, the tests and the source art live in `~` folders.

1. `Plugin~/` builds `JPOGRaptor.dll`, then NetcodePatcher patches it for RPCs. `Plugin~/JPOGRaptor.csproj.user` (gitignored, per machine) copies the DLL to `AssetBundles/` and deploys it.
2. In Unity, the prefab references the AI script in that DLL through the GUID in the committed `AssetBundles/JPOGRaptor.dll.meta`.
3. **JPOG > Build JPOGRaptor bundles** (`Editor/JPOGRaptorBundleBuilder.cs`) builds the two bundles into `AssetBundles/`. Both are committed.
4. In game, Dusk loads `jpograptormainassets.duskmod` (the mod information and content container), which registers the enemy from `jpograptorassets` (the prefab, the EnemyType and the registry assets).

## Code map

### Plugin (`Plugin~/src/`, namespace `JPOGRaptor`)

| File | What it does |
| --- | --- |
| `Plugin.cs` | BepInEx entry point. It only sets up the logger; Dusk does the registration from the bundles. |
| `JPOGRaptorAI.cs` | `JPOGRaptorAI : EnemyAI`, the whole enemy. See below. |
| `RaptorTargetingHelper.cs` | Finding and keeping a target: `TryAcquireTarget` (returns an `AcquireResult`), closest player and line of sight, the "is the player alone" check, reachability and path timeouts (`GetReachability`, `HandleTargetUnreachability`, `RetargetOrSearch`), and retargeting whoever hit the raptor. |
| `RaptorPounceHelper.cs` | The pounce: range and line-of-sight checks, `PounceRoutine` (it predicts the target's position and moves the raptor along the ground plane), the damage window, hitbox checks and `CancelPounce`. |
| `RaptorRules.cs` | Pure decision rules with no Unity types (alone check, radius, stalk cooldown, accumulating timers, hide completion). **This is the only file the tests compile.** Put new testable logic here. |

The two helpers are primary-constructor classes that take the `JPOGRaptorAI` and are created in `AssignImportantValues()`.

#### `JPOGRaptorAI.cs`

- **States** (`enum State`, used through `currentBehaviourStateIndex`): `SearchingForPlayer`, `StalkingPlayer`, `ChasingPlayer`, `RespondingToCall`, `AttackingPlayer`, `FleeingFromPlayer`, `HidingFromPlayer`. Each state's behaviour is a `case` in `DoAIInterval()`, and its movement speed is in `SeMovementSpeedPerSate()`. The tuning constants for each state (`Stalk*`, `Hide*`, `Flee*`, `HelpCallRadius`) sit at the top of the class.
- **Every frame**: `Update()` syncs the `CurrentSpeed` NetworkVariable into the Animator's `moveSpeed`, handles the death sound and stun recovery, checks off-mesh-link climbing (`CheckIfClimbing`) and ticks the helpers' timers.
- **Pack behaviour**: `CallForHelp()` → `ReactToHelpCall()` on the other raptors in `AllRaptors` (a static list), `PlayBark()` for the long and short barks, and `FindRaptorToFleeTo()`.
- **Combat**: `OnCollideWithPlayer` → `BiteAttackServerRpc`, `OnCollideWithEnemy`, `HitEnemy` (handles HP and death through `KillEnemyOnOwnerClient`), and carrying a killed player's body in the mouth (`TakeBodyInMouth*`, `DropBodyInMouth*`).
- **Animation RPCs**: `DoAnimationClientRpc(trigger)` and `SetAnimationBoolClientRPC(name, value)` drive the Animator on every client.
- **Animation event receivers**: `PlayStepClip()`, `PlayBarkClip(AudioClip)` and `PlayVoiceClip(AudioClip)` are called by events inside the `.anim` clips, not by the code. Renaming one silently breaks the clips that call it.
- **Debug logging**: `LogIfDebugBuild` is `[Conditional("DEBUG")]`, so it compiles out of Release builds.

### Unity content

| Path | Contents |
| --- | --- |
| `Content/JPOGRaptor/Prefabs/JPOGRaptor.prefab` | The enemy prefab: the AI component and its serialized fields, the Animator, the three AudioSources (`CreatureSFX`, `RaptorVoice`, `RaptorCallVoice`), collision, the scan node, the map dot and the `D_*` bone hierarchy under `Raptor/Scene_Root/D`. |
| `Content/JPOGRaptor/Registry/` | `JPOGRaptor.asset` (the EnemyType, `enemyName: Raptor`) and `JPOGRaptorEnemyDefinition.asset` (the Dusk enemy definition: key `jpog_raptor:raptor`, spawn table, bestiary text). |
| `Content/JPOGRaptor/Materials/` | Materials and the texture. |
| `Content/JPOGRaptor/Animations/`, `Audio/`, `Models/` | The Animator controller, clips, sounds and FBX. They're ripped game assets, **gitignored on `main`**, so copy them in from the host project. |
| `JPOGRaptorContentContainer.asset`, `JPOGRaptorModInformation.asset` | Dusk's content container and mod information (name, version). |
| `Package/` | The Thunderstore README, CHANGELOG and icon. |

Bundle labels: the `Content/JPOGRaptor/` folder is labelled `jpograptorassets`, and the two root `.asset` files are labelled `jpograptormainassets` (`.duskmod`). Anything added under `Content/JPOGRaptor/` goes into the content bundle.

### Animator contract

The code and the controller have to agree on these names:

- **Triggers**: `biteAttack`, `barkCall`, `shortBark`, `pounceAttack`, `stopBreath`, plus `KillEnemy`, which the base game sets in `EnemyAI.KillEnemy`.
- **Bools**: `isClimbing`. **Floats**: `moveSpeed`.
- **Layers**: Walk Layer (Locomotion blend tree, die, pounce, climbing), Main Body (breathing) and Head (bite and barks).

### Build, tests and CI

| Path | Contents |
| --- | --- |
| `Editor/JPOGRaptorBundleBuilder.cs` | Builds only this mod's bundles. It force-reimports `Content/` and refuses to build if an enemy prefab has a missing script or a null `enemyType`. Batch mode: `-executeMethod JPOGBuild.JPOGRaptorBundleBuilder.Build`. |
| `Plugin~/JPOGRaptor.csproj` | The version (`<Version>`), package references (BepInEx, DawnLib 0.9.x), the NetcodePatch target and the Release-only Thunderstore pack. With `CI=true` it takes the game assemblies from `LethalCompany.GameLibs.Steam`; otherwise it uses `$(ManagedDirectory)`. |
| `Plugin~/Thunderstore/thunderstore.toml` | The Thunderstore package layout: DLL and `.duskmod` in `plugins/JPOGRaptor/`, the content bundle in `plugins/JPOGRaptor/Assets/`. |
| `Tests~/JPOGRaptor.Tests/` | xUnit tests on net8.0. They link only `RaptorRules.cs`. |
| `.github/workflows/` | `build.yml` (test, build, pack), `release.yml` (tags and creates a GitHub Release when `<Version>` changes), `thunderstore.yml` (manual publish). |
| `AssetSources~/` | The Blender source. |

## Commands

```bash
# Tests (no game install needed)
dotnet test 'Tests~/JPOGRaptor.Tests/JPOGRaptor.Tests.csproj'

# Plugin build like CI (no game install needed)
cd 'Plugin~' && dotnet tool restore && dotnet build -c Debug -p:CI=true

# Local build that also deploys (needs Plugin~/JPOGRaptor.csproj.user)
cd 'Plugin~' && dotnet build
```

You can't build bundles outside Unity. CI packages the bundles committed in `AssetBundles/`.

## Things that are easy to break

- **Multiplayer**: the host owns the raptor, and the AI (`DoAIInterval`, the pounce, targeting) runs on the owner. Anything every client must see (animation, sound, a carried body, blood) has to go through a ClientRpc sent by the server, or through a NetworkVariable. Don't add a `NetworkAnimator`: the triggers already go out through `DoAnimationClientRpc`, so a second sync would double them. Each new RPC needs NetcodePatcher, which the csproj runs after the build.
- **Hosts and clients run the same prefab**, so adding or removing a NetworkBehaviour, an RPC or a NetworkVariable makes old and new versions incompatible.
- **Prefab and DLL**: open Unity only after `JPOGRaptor.dll` exists in `AssetBundles/`. Otherwise Unity drops its `.meta`, and the prefab loses its AI script and every serialized field. A field you rename or remove on `JPOGRaptorAI` loses its prefab value too (use `[FormerlySerializedAs]`).
- **Animation events**: see the receivers above. Changing a clip's length or sample rate moves its sounds.
- **Bundles are committed binaries**: rebuild them after any change under `Content/` or to the Dusk assets, and commit them with that change.
- **Releasing**: raise `<Version>` in the csproj and the version in `JPOGRaptorModInformation.asset` together, and add a `## <version>` entry to `Package/CHANGELOG.md`. The workflows check all three.
- **`.meta` files**: commit the `.meta` with every asset, and never regenerate a GUID that something references.
- **Generated or ignored folders**: don't commit the gitignored folders (`bin/`, `obj/`, `Thunderstore/Packages/`, the DLL, `*.csproj.user`, or the ripped `Animations`, `Audio` and `Models`).

## Conventions

- C#: `netstandard2.1`, `LangVersion latest`, nullable enabled. Match the style of the file you're editing.
- Commits: short imperative subject, a body that explains why, and `Refs LC-<n>` for the Linear issue.
- PRs: describe the change, list the testing done (say so when it wasn't tested in game), and end with `Linear: LC-<n>`.
- Branch from `main`. The Linear-generated branch names look like `teunbuis17linear/lc-<n>-<slug>`.
