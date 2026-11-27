## 1.0.7
The Raptor's behaviour has been reworked. It now hunts more like a pack predator.
- Targeting:
    - Raptors now go for players who are alone. Players standing close to other players are ignored.
    - A player who gets too close, or who shoots a raptor, is attacked right away.
- Stalking:
    - Raptors now sneak up on their target and pounce when close, instead of calling for help.
    - They stay out of sight while stalking. If a player spots them, they hide and start searching again.
- Fleeing:
    - A raptor that misses its pounce now runs back to the pack, calls for help with a long bark, and then attacks again.
    - Raptors that answer the call do a short bark.
    - Help calls only reach raptors that are nearby.
- Fixes:
    - Fixed the raptor spinning in place when it stood on top of a player.
    - Fixed some cases where a raptor could lose its target when answering a call.
    - A pounce only hits the player it was aimed at.

## 1.0.6
Updated for V81
Switched to DawnLib
Fixed teeth texture for the raptor

## 1.0.5
Updated to v73
The raptors foot steps might be too quiet, after updating I tried setting the radius higher which helps a bit.
I might fine tune it in the future. For now, the raptor will just make less waling noise and be more stealthy...

## 1.0.4
Fixed the raptor doing too much damage to other enemies.

## 1.0.3
Two minor fixes
- Small fix for the raptors targetting when its target player becomes untargetable.
    - It should now try to switch to another valid target.
- Fixed the raptor not being able to bite after a pounce attack

## 1.0.2
A lot of minor fixes
- General:
    - Added more layers to the Raptor's NavMesh.
        - It should now be able to reach more places.
    - The Raptor should no longer play the hit sound effect once it's dead and gets hit.
- Chasing:
    - No longer attempts to reach/chase players inside the ship if it is not in the ship itself.
    - Now retargets players if it gets hit.
    - Added a timeout for the Raptor if chasing a player that is unreachable for too long.
- Pounce:
    - The Raptor should now check for a clear path before attempting to pounce on the player.
    - Improved pounce animation.
- Animation:
    - Changed walking animation to a blend tree.
        - Walking and running transitions should now look much smoother.
    - Added an animation for when the Raptor is climbing.
        - The current animation is actually a grapple animation, but it works okay for now.

## 1.0.1
Small fix.
- Fixed the wrong assetbundle being packaged.
    - This should make the Raptor be able to load in game.
    - This could also update some of its behaviour as the wrong/old assets were bundled.

## 1.0.0
This is the first release of the JPOGRaptor.
It is still WIP but should function good enough as an enemny ingame.
- Initial release.
- Enjoy!