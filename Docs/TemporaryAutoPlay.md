# Temporary room auto battle

In ChessRoomBlockout, entering battle attaches BattleAutoPlay and displays a temporary top-left panel. Auto battle attacks using the first prototype ability and moves toward enemies using legal cells. Skip hero turns supports defeat checks. Click the active button again to stop. Pause suspends the driver; leaving battle resets it.

Commands use PartyChainController, including movement and projectile playback. No health, damage or charge values are changed. Victory is not guaranteed; the driver stops after 600 decisions to bound stalemates. It also stops when either team has no health remaining. This is a test observation, not implementation of the game's end-of-battle flow, which remains disabled in the ordered battle session.

Compilation and Play Mode must be verified in the installed Unity editor; the previously recorded editor path was unavailable during implementation.
