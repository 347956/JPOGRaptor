## 1.0.2
A lot of minor fixes
- General:
    - Added more layers to the navMesh of the Raptor.
        - It should now be able to reach more places.
    - The Raptor should no longer play the hit sound effect once its dead and gets hit.
- Chasing:
    - No longer attempt to reach/chase players inside the ship if it is not in the ship itself.
    - Now retargets players if it gets hit.
    - Added a time-out for the raptor if chasing a player too long that is unreachable.
- Pounce:
    - The Raptor should now check if it has a clear path before attempting to pounce on the player
    - improved pounce animation.
- Animation:
    - Added an animation for when the raptor is climbing.
        - The currenct animation is actually a grapple aniamtion but it works OK for now.    

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