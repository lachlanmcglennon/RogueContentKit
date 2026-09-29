# RCK.Interactions features

RCK.Interactions adds compatibility traits for NPC buttons, language understanding, Hacking Tool outcomes, ambient sounds, and map markers.

## Interaction button traits

| Trait | Player-facing button | How it behaves |
| --- | --- | --- |
| `Heal_Player` | Heal | Normal Heal price. |
| `Administer_Blood_Bag` | Administer Blood Bag | Costs 20 HP from the NPC. |
| `Use_Blood_Bag` | Use Blood Bag | Shown when the player has a Blood Bag. |
| `Give_Blood` | Give Blood | Costs health and pays cash; the Low Health mutator lowers the health cost. |
| `Identify` | Identify | Normal syringe-identify price. |
| `Bribe_for_Entry_Alcohol` | Bribe with Beer or Whiskey | Uses Beer first, then Whiskey. |
| `Pay_Entrance_Fee` | Pay Entrance Fee | Normal bribe price. |
| `Leave_Weapons_Behind` | Leave Weapons Behind | Shown when the player carries weapons. |
| `Play_Bad_Music` | Play Bad Music; Play Mayor Evidence when relevant | Normal vanilla prices. |
| `Buy_Slave` | Purchase Slave | Uses the normal slave price or the rescue-quest slave price. |
| `Manage_Chunk` | Buy Key, Buy Safe Combination, or Buy Hotel Key | Uses vanilla key and safe-combination prices. |

Other interaction traits add their named vanilla-style buttons or gates:

| Traits | What they do |
| --- | --- |
| `Administer_Blood_Bag`, `Borrow_Money`, `Borrow_Money_Moocher`, `Bribe_Cops`, `Bribe_for_Entry_Alcohol`, `Buy_Round`, `Election_Results`, `Give_Blood`, `Heal_Player`, `Identify`, `Influence_Election`, `Leave_Weapons_Behind`, `Pay_Big_Quest`, `Pay_Debt`, `Pay_Entrance_Fee`, `Play_Bad_Music`, `Use_Blood_Bag` | Offer their matching vanilla interaction buttons. |
| `Buy_Slave`, `Election_Badge`, `Election_Signup`, `Manage_Chunk`, `Offer_Motivation` | Use the nearest matching vanilla interaction flow. |
| `Teach_Languages` | Adds language lesson buttons. |
| `Cop_Access`, `Honorable_Thief` | Lets interaction buttons depend on the player or NPC type. |
| `Untrusting`, `Untrustinger`, `Untrustingest` | Hides sensitive buttons until the relationship or language checks allow them. |
| `Insular`, `Insularer`, `Insularest` | Hides sensitive buttons until the relationship or language checks allow them. |
| `Panhandler` | Lets the player request money from NPCs. |
| `Polyglot`, `Speaks_Binary`, `Speaks_Chthonic`, `Speaks_ErSdtAdt`, `Speaks_Foreign`, `Speaks_High_Goryllian`, `Speaks_Undercant`, `Speaks_Werewelsh` | Adds language understanding. `Polyglot` understands all supported languages. |
| `Explode`, `Go_Haywire`, `Tamper_with_Aim` | Adds Hacking Tool outcomes for the named target. |
| `Choochootations`, `Computation_Noises`, `Conveying_Noises`, `Cop_Bot_Sound`, `Fire_Noises`, `Generating_Overclocked_Sounds`, `Generating_Sounds`, `Movie_Screen_Sounds`, `Powering_Noises`, `Sawblade_Sound`, `Ventulations` | Starts a looping vanilla-style ambience sound on the NPC. |
| `Squeakitations`, `Whhhhhhhh`, `Woof`, `Wummmmmm`, `Zzzzzzzzzzzz` (closest-match clips) | Starts a looping vanilla-style ambience sound on the NPC. |
| `MapMarker_Pilot` | Forces a minimap marker for the NPC. |

`Teach_Languages` can teach the supported languages. `Offer_Motivation` uses RCK's custom motivation button. `Explode`, `Go_Haywire`, and `Tamper_with_Aim` are Hacking Tool outcomes.

## Language speakers

Language traits let a player and NPC understand each other when either side speaks the matching language. These vanilla agents are treated as native speakers:

- Binary: `Robot`, `CopBot`, and `Mech`.
- Chthonic: zombies, `Zombie`, `Ghost`, `Vampire`, and `ShapeShifter`.
- ErSdtAdt: `Alien` and `Slavemaster`.
- Foreign: `Alien` and anyone with `CantSpeakEnglish`.
- High Goryllian: `Gorilla`.
- Undercant: `Thief`, `Gangbanger`, `Mobster`, and `Cannibal`.
- Werewelsh: `Werewolf`.

`Cop_Access` counts enforcers, `Cop*` agents, `Soldier` agents, and `TheLaw` holders as cops. `Honorable_Thief` counts `Thief`, `Gangbanger`, and `Mobster`.

## Ambient audio traits

| Trait | Sound |
| --- | --- |
| `Sawblade_Sound` | SawBlade |
| `Generating_Sounds` | Generator |
| `Generating_Overclocked_Sounds` | Generator2 |
| `Computation_Noises` | Computer |
| `Conveying_Noises` | ConveyorBelt |
| `Fire_Noises` | FlameGrate |
| `Powering_Noises` | PowerBox |
| `Movie_Screen_Sounds` | MovieScreen |
| `Ventulations` | AirConditioner |
| `Choochootations` | Train |
| `Cop_Bot_Sound` | CopBot |
| `Squeakitations` | closest matching vanilla loop |
| `Whhhhhhhh` | closest matching vanilla loop |
| `Wummmmmm` | closest matching vanilla loop |
| `Zzzzzzzzzzzz` | closest matching vanilla loop |
| `Woof` | closest matching vanilla loop |

The five closest-match sounds are `Squeakitations`, `Whhhhhhhh`, `Woof`, `Wummmmmm`, and `Zzzzzzzzzzzz`.
