# The Albedo Gambit

An early isometric 3D game prototype in Unity. A party of four characters explores locations; story-driven tactical encounters will introduce chess-inspired movement and abilities.

The project currently tests party control, Rigidbody movement, and a reusable battle scene. [ChessRoomBlockout](Assets/Scenes/ChessRoomBlockout.unity) is the development playground. [GameplayTemplate](Assets/Scenes/GameplayTemplate.unity) provides a starting point for new levels, and [Level02Prototype](Assets/Scenes/Level02Prototype.unity) tests the same gameplay prefabs with an imported Blender environment. See the [stage-one handoff](Docs/Stage1Handoff.md) for the current scope and limitations.

## Characters

| Character | Color | Chess-inspired role |
| --- | --- | --- |
| A | White | King |
| B | Red | Rook |
| C | Green | Bishop |
| D | Blue | Knight |

## What you can try

- Free movement with Rigidbody physics and sequential jumps.
- Party following in A → C → B → D order, obstacle avoidance, and catch-up behavior.
- Splitting the party, controlling individual characters, and regrouping around A.
- A test room with an 8 × 8 board and obstacle cubes.
- A debug switch between exploration and battle. The board is hidden during exploration and appears in battle; it has no physical collision.
- Character selection, legal-move highlights, and mouse-driven movement using the king, rook, bishop, and knight patterns. The first action locks the active hero until **End Turn**; each turn allows one movement.
- A Figma-based battle HUD with four character cards, health, **Menu / Resume**, and **End Turn**. Abilities and mana are placeholders.
- Four black enemy capsules spawn on A3–A6 opposite the heroes on H3–H6. They disappear on exit or reset. Enemy actions are not implemented yet, so **End Turn** currently passes the enemy side.

Attacks, magic, enemy behavior, and cutscenes are not yet connected to the scene. In the planned game, story events will start tactical encounters; simply walking onto the board will not change modes.

## Getting started

1. Clone the repository with Git LFS support, for example through GitHub Desktop. If prompted, choose **Initialize Git LFS**.
2. Add the project folder to Unity Hub and open it in **Unity 6000.3.8f1 (Unity 6.3 LTS)**.
3. Wait for asset import, then open `Assets/Scenes/ChessRoomBlockout.unity`.
4. Press **Play** and focus the **Game** window.

## Controls

| Input | Action |
| --- | --- |
| WASD / arrow keys | Move relative to the camera |
| Space | Jump; party members jump in sequence while following |
| G | Split the party, regroup around A, or cancel regrouping |
| 1–4 | Select a character while the party is split |
| T | Toggle the test battle mode |
| R | Reset the party to its starting positions |
| Left-click a hero or occupied cell | Select a hero in battle |
| Left-click a green highlighted cell | Move to a legal destination |
| Esc | Pause or resume |
| Left-click a portrait | Select a hero before taking an action |
| Menu / Resume | Open the pause menu or resume |
| End Turn | Finish the current test turn |

## Project layout

- `Assets/Scenes` — the blockout, reusable template, and Blender-based level prototype.
- `Assets/Prefabs/Prototype` — reusable party, board, and enemy prefabs.
- `Assets/Prefabs/UI` — the battle HUD; see its [setup notes](Docs/BattleHUD.md).
- `Assets/Scripts/Prototype` — movement, physics, party following, and board rules.
- `Assets/Editor` — prototype setup and validation tools.
- `Docs` — development decisions and technical notes.

For movement setup and scene reuse, see the [party and board prototype notes](Docs/PartyPrototype.md).
