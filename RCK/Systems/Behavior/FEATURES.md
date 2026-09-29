# RCK.Behavior features

RCK.Behavior gives custom NPCs compatibility traits for item seeking, target seeking, vigilance, sight, and hearing.

## NPC behaviors

| Trait | What it does |
| --- | --- |
| `Accident_Prone` | This NPC uses loose pathing and may take dangerous routes through hazards. |
| `Brainless` | This NPC loses sight, hearing and noise vigilance, so it barely reacts to the world. |
| `Concealed_Carrier` | This NPC hides its weapon while calm, draws it in a fight, then holsters it after a short lull. |
| `Seek_and_Destroy` | This NPC periodically hunts visible nearby players and attacks them. |

## NPC behaviors / LOS / Agent

| Trait | What it does |
| --- | --- |
| `Eat_Corpses` | This NPC seeks edible bodies it can see and starts the cannibalize action. |
| `Pick_Pockets` | This NPC looks for valid targets and starts the vanilla pickpocket action. |
| `Suck_Blood` | This NPC looks for valid targets and starts the vanilla blood-sucking action. |

## NPC behaviors / LOS / Item

| Trait | What it does |
| --- | --- |
| `Grab_Alcohol` | This NPC grabs nearby beer, whiskey and cocktail-style items it can see. |
| `Grab_Drugs` | This NPC grabs nearby drug and syringe-style items it can see. |
| `Grab_Everything` | This NPC grabs most safe items it sees; Accident-Prone lets it risk traps and armed bombs too. |
| `Grab_Food` | This NPC grabs nearby food and healing items it can see. |
| `Grab_Money` | This NPC grabs nearby money it can see. |

## NPC behaviors / Vigilance

| Trait | What it does |
| --- | --- |
| `Vigilant` | This NPC uses vanilla noise vigilance level 1. |
| `Vigilanter` | This NPC uses vanilla noise vigilance level 2. |
| `Vigilantest` | This NPC uses vanilla noise vigilance level 3. |

## Senses

| Trait | What it does |
| --- | --- |
| `Deaf` | This NPC's hearing range is set to zero. |
| `Sharp_Hearing` | This NPC gets an extended hearing range. |
| `Dolphin_Ears` | This NPC gets a very long hearing range. |
| `Hack_Sensor` | This NPC gets an extended hearing range like a sensor. |
| `Owl_Ears` | This NPC hears only at short range. |
| `Snake_Ears` | This NPC's hearing range is set to zero. |
| `Eight_Beamed` | This NPC sees all around itself with a 360-degree cone. |
| `Vision_Beam_Normal` | This NPC uses the normal 96-degree sight cone. |
| `Cyclops_Eye` | This NPC sees in a narrow 30-degree cone. |
| `Falcon_Eyes` | This NPC sees in a focused 60-degree cone. |
| `Horse_Eyes` | This NPC sees in a wide 180-degree cone. |
| `Mantis_Eyes` | This NPC sees in a very wide 270-degree cone. |
| `Xenops_Eyes` | This NPC sees all around itself with a 360-degree cone. |
| `Visually_Blind` | This NPC's sight range is set to zero. |
| `Visually_Disabled` | This NPC gets a very short sight range. |
| `Visually_Impaired` | This NPC gets a short sight range. |
| `Visually_Sharp` | This NPC gets an extended sight range. |
| `Visually_Vigilant` | This NPC gets a long sight range. |
| `Visually_Zenithal` | This NPC gets a very long sight range. |

Legacy names

Accepted from older content and have no effect on their own: `Keen_Ears`, `Keener_Ears`, `Keenest_Ears`.
