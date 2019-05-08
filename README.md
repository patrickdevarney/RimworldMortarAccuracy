# RimworldMortarAccuracy

Non-Steamworks version of mod found here https://steamcommunity.com/sharedfiles/filedetails/?id=1729446857

### Installation
Copy the entire MortarAccuracy folder into your Rimworld/Mods directory. Ex:
C:\Program Files (x86)\RimWorld\Mods\

Launch Rimworld, enable Mortar Accuracy in mod menu. Can access more settings in Options/Mod Settings/Mortar Accuracy from main menu in-game.

### Description
A collection of options to make mortars more/less accurate. Customizable in the Mod Settings menu. These changes affect all mortar shots, including enemy mortars. Changes include:
Weather: Mortar accuracy is affected by weather (in vanilla, mortar shots rarely are affected by weather)
Colonist Skill: Mortar accuracy is affected by colonist Intellectual and/or Shooting skill (in vanilla, skill makes no difference)
Cover Miss Chance Removed: Mortars no longer have a chance to miss due to cover surrounding the target (in vanilla, target's cover causes missed shots)
Can customize how much high-skill colonists can improve mortar accuracy, and how much low-skill colonists hurt accuracy
By default, a colonist with maximum skill will improve a mortar's accuracy to 75%. A colonist with no skill will affect a mortar's accuracy by -40% of it's normal value. These values can be changed in Mod Settings (can set all skill-levels to have 100% perfect accuracy if desired)

### Saved Games and Compatibility
Can be freely added/removed from saved games.
[WD] Expanded Artillery: Accuracy affects new mortars and cannons
More Vanilla Turrets 1.0: Accuracy affects manned rocket turret, manned blast charge turret, and devastator mortar

### Known Issues
Combat Extended: Incompatible! User-reported issues. Looks like CE replaces mortar shooting with its own logic so it will currently override this mod or some of your mortars will not fire depending on your load order. Patch is unlikely with CE being such a large overhaul.
[XND] TE Turret Expansion: This mod also affects mortar accuracy so it is likely that whatever mod is higher in your load order will be overwritten. Patch is likely.

### Multiplayer
99% sure this works in multiplayer, but I have yet to test it.

### Translations
English, Spanish (Latin American), Russian (thanks Ninja Kitten)

### Future Plans
When choosing targets, show radius outline of where the projectile will land
Leading targets: In vanilla, it looks like colonists fire directly on the tile/target assigned. This requires the player to lead the target manually which is frustrating. Instead, factor in distance from mortar, velocity of projectile, and velocity/path of the target to have colonists lead their targets automatically.
Option for Shooting skill to improve mortar reload time
Option for AI select best pawn for mortar duty
