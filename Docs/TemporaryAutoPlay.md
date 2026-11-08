# Temporary room auto battle

## Last enemy knockout fixture (Editor only)

During battle click `Load last enemy KO test (resets battle)`. This replaces the current encounter with the usual room formation, moves enemy 4 to G4 with 30 HP, sets enemies 5–7 to zero HP and gives Mia the turn at H4. Select either ordinary ability and click the enemy at G4. The projectile uses the normal attack path. The panel reports `All enemies down` after impact; the game victory screen is still separate unfinished work. This checks logical knockout; the existing enemy view has no dedicated knockout animation. Exit Play Mode to discard the fixture state. The fixture button and session setup are excluded from player builds.

Verified directly against BattleSession: setup, legal ordinary attack, last enemy at zero HP, all enemies knocked out, charge increment and no further attack target. Full Unity compilation and visual Play Mode verification remain pending.

Enemy movement in ChessRoomBlockout now picks the second-ranked legal destination with a default probability of 30%, adjustable in the test panel. Ranking uses distance to the nearest hero, then travel distance, then cell index. Equal-quality alternatives are possible. With only one legal move it uses that move; with none it passes. Attacks retain the existing policy. Other scenes retain the best destination. This option does not guarantee a hero victory or validate the victory flow by itself.

In ChessRoomBlockout, entering battle attaches BattleAutoPlay and displays a temporary top-left panel. Auto battle attacks using the first prototype ability and moves toward enemies using legal cells. Skip hero turns supports defeat checks. Click the active button again to stop. Pause suspends the driver; leaving battle resets it.

Commands use PartyChainController, including movement and projectile playback. No health, damage or charge values are changed. Victory is not guaranteed; the driver stops after 600 decisions to bound stalemates. It also stops when either team has no health remaining. This is a test observation, not implementation of the game's end-of-battle flow, which remains disabled in the ordered battle session.

Compilation and Play Mode must be verified in the installed Unity editor; the previously recorded editor path was unavailable during implementation.
