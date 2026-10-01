# RCK.Items features

The game ships a set of items that have a sprite, a name and usually a description, and that campaigns, merchants and
the level editor can hand out, but that do nothing when used. RCK.Items gives each of them a job. It's always on and
needs no trait. Every fix checks the game's own code at startup: if a game update starts handling an item itself, RCK
logs it and leaves that item to the game.

Item names are the game's own, so campaigns can place them, put them in containers or give them to custom characters
with the usual item names below. The Sniper Rifle fix lives in [Combat](../Combat/FEATURES.md#sniper-rifle).

| Item name | What it does now |
| --- | --- |
| `BFG` | Fires a slow rocket that bursts in a Huge explosion. |
| `GrapplingHook` | Pulls you to a wall or solid object, or pulls a person to you. Never runs out. |
| `TripMine` | A thrown trap that arms itself and goes off when an enemy comes near. |
| `ForceField` | 8 seconds of invincibility. |
| `MagicLamp` | Grants one random wish. |
| `VoodooDoll` | Takes over one person for 30 seconds, like the Mind Control ability. |
| `AugmentationCanister` | Gives one completely random trait at an Augmentation Booth. |
| `FiveLeafClover`, `SixLeafClover` | Stronger versions of the Four-Leaf Clover's luck. |
| `MoodRing` | Warns you about people nearby who secretly mean you harm. |
| `Rag` | Combine with a Beer or a Whiskey to make a Molotov Cocktail. |

### BFG

The game defines it (3 rounds, heavy recoil) but never fired it.

- Each shot fires one rocket, like the Rocket Launcher, that bursts in a Huge explosion (the Rocket Launcher's is
  Normal). It uses one round, with about 1.2 s between shots.
- Strong recoil pushes the shooter back (not with the Knockback-less traits), and the screen shakes. A Silencer makes
  it quiet.
- NPCs who carry one fire it too. Every machine in multiplayer sees the same rocket.

### Grappling Hook

The game defines it as a gun with no shot.

- Fire it at a wall or a solid object within 8 tiles to pull yourself to it. Fire it at a person to pull them
  to you. Heavy targets (an empty or occupied mech, a bodyguarded NPC) act as anchors, so you're pulled to them.
- A thin rope shows between you and the hook while it pulls. The pull stops if you or they hit something.
- It never runs out (no ammo count); there's about 0.8 s between shots.
- Pulling a neutral NPC annoys them a little. NPCs who carry one can use it too.

### Tripmine

The game gave it no image and no effect.

- It gets its sprite. Throw it and it lands, arms itself after 1 second (with the armed-mine sound) and can no longer be
  picked up.
- It goes off in a Land Mine explosion when anyone who isn't the thrower or their ally (Aligned, Loyal or Submissive)
  comes within about 2 tiles, when a thrown item or pushed object hits it, or when fire reaches it.
- It ignores characters who jump over it or don't trigger floor hazards. Unlike the game's own version, NPCs don't
  know to steer clear of it.

### Force Field

Using it makes you invincible for 8 seconds (the game's Invincible effect). One is used up per use.

### Magic Lamp

Rubbing it grants one wish at random, then the lamp is spent. The wishes are 250 money, full health (only if you're
hurt), an extra life (only if you don't already have a lasting one) and a boost to all your stats (the game's Increase
All Stats effect). A message says which wish came true.

### Voodoo Doll

The game had a description for it but no way to use it.

- Click it, then click a person within range to take them over for 30 seconds, as with the Mind Control special
  ability. The doll is used up.
- It can't take over players, the dead, ghosts, bodyguarded or mind-control-proof characters, anyone already under
  control, or anyone with the Mind Control ability.
- Control ends when the game's "Mind Controlling" effect runs out or when you die.
- Its price is raised from 5 to 60.

### Augmentation Canister

The game's description says to use it at an Augmentation Booth, but the booth had no way to take it.

- With a canister in your inventory, an Augmentation Booth shows a **Use Augmentation Canister** button. It uses up
  the canister and gives you one completely random trait. It could be anything, good or bad, including traits you
  haven't unlocked.
- It never gives a trait you already have (or its upgrade), an upgrade trait, a trait that can't be swapped, or a
  trait the booth's own rules wouldn't allow (special-ability traits you can't use, conflicts). If no trait fits, the
  canister is kept.
- Clicking the canister itself reminds you to use it at a booth.

### Five-Leaf and Six-Leaf Clovers

Carrying one works like the game's Four-Leaf Clover, only stronger: the Five-Leaf adds twice and the Six-Leaf three
times the Four-Leaf's bonus to each luck roll (critical hits, hacking, slot machines, free shop items and so on). They
stack with each other and with the Four-Leaf Clover. Players only, as in the game.

### Mood Ring

The game described it as useless. Worn like armour, it now watches people within about 12 tiles that you can see, and
shows **Bad vibes...** over anyone who secretly means you harm: someone who hates you in secret (the hidden enemies some
character traits create), a hidden werewolf or a hidden shapeshifter. It
warns about the same person at most once every 15 seconds.

### Rag

Drag a Rag onto a Beer or a Whiskey in your inventory to make a Molotov Cocktail. One of each is used up.

### Merchants

RCK merchant types now stock some of these items: `Fire_Sale` and `Hardware_Store` sell the Rag, `Hardware_Store`
the Grappling Hook, `Occultist` the Magic Lamp, `Pawn_Shop` the Five-Leaf Clover, `Tech_Mart` the Augmentation
Canister and `Upper_Cruster` the Six-Leaf Clover. Items that still do nothing in the game (such as the Blowtorch,
Matches, Map, Rope, Ballet Shoes, Vision Detector, Sticky Mine and Laser Blazer) were taken off the shelves.

### Not tested yet

These fixes are checked against the game's code but haven't been played yet. Multiplayer and the Grappling Hook's rope
are the least certain; report anything odd with `BepInEx\LogOutput.log`.
