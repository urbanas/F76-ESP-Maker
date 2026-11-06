ESP Maker 2.1 - make your own ESP mod for Fallout 76
==================================================

ESP Maker lets you pick what you want to spot in Fallout 76 - bobbleheads, magazines, plans,
XP food, pre-war food, notes, plants, loot, loot bags, CAMP storage boxes, clothes, event objects, players' vending machines,
workbenches, legendary enemies, any kind of creature - and how it should
look: an outline glow (also through walls), a beam into the sky, a flat icon or a little 3D model
above it, or simply brighter colours. It then builds the mod (.ba2) for you and installs it.

Everything is client-side and visual only: the app writes a normal .ba2 mod and lists it in your
custom ini, like any other Fallout 76 mod.


REQUIREMENTS
- Windows 10 or 11 (64-bit).
- Fallout 76 on PC: Steam, or the Microsoft Store / PC Game Pass version (Xbox app).
  Consoles can't use mods.
- Nothing else to install.


INSTALL
1. Unzip anywhere (e.g. your Desktop or Documents) and start "ESP Maker.exe".
2. Windows may show "Windows protected your PC" (the app isn't signed with a paid certificate):
   click "More info" -> "Run anyway".
3. The first start reads the game's item list (a few seconds; again after game updates) and shows
   a short tour, then offers ready-made presets.

The app finds the game by itself. If it can't, it asks for the folder:
- Steam: ...\steamapps\common\Fallout76
- Microsoft Store / Game Pass: e.g. C:\XboxGames\Fallout 76


HOW TO USE
1. Pick a section in the bar on the far left, then a category - or search (Ctrl+K jumps to any
   category or item). Select what you want to find.
2. Click the category to choose its look in the pop-up (ready-made look, colour, icon). Click an
   item for its own look. The pop-up stays open and follows the row you pick; drag it anywhere.
   The Name card puts a floating name above the item; Hide after makes the highlight shrink
   away after a while.
   The 3D preview at its bottom shows the real model the way the mod will show it.
   "Original" shows the plain game model, "Apply look" goes back (preview only).
   "Bigger view" opens a large 3D window: walk around, add a person, a house or walls, change
   the light, check colour-blind views, compare other selected items side by side.
3. Close the game, click "Build & save", type a name for your mod, click Build, start the game.
   Uncheck "Install in the game" to only make the .ba2.
Changed something? Click "Build & save" again and pick the same name to replace the mod.
Ctrl+Z / Ctrl+Y undo and redo. View -> Dark mode, Compact rows.

Edit -> "Add presets...": Enemies, Legendary enemies, Bobbleheads, Magazines, Plans, the Slasher
event, XP buffs, Player vendors and Bright plants in one click.
Several mods: each name you build under is its own .ba2 - e.g. one for enemies, one for
collectibles - and they work side by side. The bottom of the window lists your mods in the game.
"Import .ba2..." opens a mod so you can change it, yours or someone else's. With something already
selected it asks whether to replace that or add to it (that is how you merge mods into one).
Other people's mods: models that aren't in ESP Maker's lists can't be included (the log lists them).
Turn the original mod off in your mod manager once yours is built.


UPDATE TO A NEW VERSION
Close the app, replace "ESP Maker.exe" with the new one and start it - your setups live in
%AppData%\ESP Maker and stay. To get the fixes into a mod, import it ("Import .ba2...") and click
"Build & save" under the same name. See CHANGELOG.txt for what changed.


UNINSTALL
File -> "Uninstall from the game..." lets you pick which mods to remove, with their ini entries.
Then delete the app folder, and %AppData%\ESP Maker if you don't want to keep your setups.


GOOD TO KNOW
- Changes show after a game restart (the game reads mods only when it starts).
- The game highlights 3D models, not items: things that share a model light up together (hover an
  item to see what shares it). Magazines share a few covers; plans share two models.
- Only things lying in the world can glow - loot inside containers and bodies can't.
- CAMP storage boxes and loot bag skins light up for every player, not only yours. Clothes glow
  only while lying on the ground, never on players wearing them.
- Enemies that turn invisible (cloaking) can't be highlighted while cloaked.
- Plants lose their beam once harvested.
- Bright colours changes the game's material, not the item: everything using that material brightens.
- NPC vendors can't be highlighted: they share their models with ordinary robots and players' clothes.
- If you also use other ESP mods: when two mods change the same model, the one listed later in the
  ini wins. ESP Maker lists its mods last.
- Your settings and setups: %AppData%\ESP Maker. The game's ini: Documents\My Games\Fallout 76\
  (Fallout76Custom.ini on Steam, Project76Custom.ini on the Microsoft Store / Game Pass version).
  The first change makes a backup next to it (.before-espmaker).


PROBLEMS?
If something goes wrong, the app writes %AppData%\ESP Maker\error.log - please attach it to your
bug report, together with what you did. "Show log" (bottom right) shows what the last build did.
