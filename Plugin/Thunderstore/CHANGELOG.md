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