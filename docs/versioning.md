# Versions and updates

The RCK Pack's version is RCK's version: `RCK-Pack-1.2.3.zip` holds RCK 1.2.3. Versions follow
[Semantic Versioning 2.0](https://semver.org/): **MAJOR.MINOR.PATCH**. Every release's changes are in
[CHANGELOG.md](../CHANGELOG.md), and the full notes (English, Russian and Chinese) are on the
[Releases](https://github.com/lachlanmcglennon/RogueContentKit/releases) page.

## What a version change means for you

| Change | Example | What it contains | For players | For campaign makers |
|---|---|---|---|---|
| **Patch** | 1.2.0 → 1.2.1 | Fixes only. No new or renamed traits, mutators, API members or data formats. | Update any time. | Campaigns work as before. |
| **Minor** | 1.2.1 → 1.3.0 | New traits, mutators, features and API, added alongside the old ones. | Update any time. Your characters, saves and campaigns keep working. | A campaign that uses something new needs at least this version; say so (see below). |
| **Major** | 1.3.0 → 2.0.0 | Something is removed or renamed (a trait ID, a mutator or a name kept from CCU), the save or campaign data format changes, the RCK API changes incompatibly, or the pack needs a different major version of RogueLibs or BepInEx, or a different game build. | Read the release notes before updating. | Some campaigns may need changes; the notes say which names changed. |

Names kept from CCU ([the list](legacy-ccu-vocabulary.md)) are only ever removed in a major release.

## The other parts of the pack

RogueLibsPlus (RL+) is a separate library with its own version number, which changes only when RL+ itself changes.
Each release's notes list the versions of all four parts: RCK, RogueLibsPlus, RogueLibs and BepInEx. The zip's name
uses the RCK version.

## Which versions get fixes

Only the newest release is supported. Fixes go into the newest minor version: once 1.3.0 is out, a fix comes as
1.3.1, not 1.2.2. Please update to the newest release before reporting a problem.

## Pre-releases

Versions such as `1.3.0-beta.1` or `1.3.0-rc.1` are test builds for people who want to try changes early.

- **beta**: the new features may still change.
- **rc** (release candidate): becomes the final release unless a problem is found.
- Pre-releases are marked **Pre-release** on the Releases page and are never "Latest". The order is
  `1.3.0-beta.1` < `1.3.0-beta.2` < `1.3.0-rc.1` < `1.3.0`.
- A campaign shouldn't require a pre-release: use the final version number.

## The version on the main menu

The bottom left of the main menu shows the installed versions, for example `RL+ v1.0.0` and `RCK v1.0.0`. The same
text is in `BepInEx\LogOutput.log`.

- `RCK v1.0.0` is the 1.0.0 release.
- `RCK v1.0.0+3.gabc1234` is a test build, made 3 commits after the 1.0.0 release, from commit `abc1234`. `.dirty`
  at the end means it was built with changes that weren't committed yet. Test builds are not
  releases.
- `RCK v1.0.0+unknown` was built outside a git checkout, for example from a "Source code" zip.
- BepInEx's plugin list and the DLLs' file properties show the plain number (`1.0.0`), because BepInEx only accepts
  numbers there.

When you report a problem, copy the whole menu line.

## For campaign makers: newer names on an older RCK

RCK doesn't check versions, so a campaign can't require a minimum RCK version. This is what happens when a campaign
uses a trait or mutator that the installed RCK doesn't know (for example, one added in a newer RCK):

- **NPC traits:** the NPC still gets the trait, but nothing acts on it, so it behaves as if it didn't have it. There
  is no error or warning. Where the game shows the trait's name, it shows the raw ID with an `E_` prefix.
- **Mutators** (for the whole campaign or for one level): the mutator is kept in the list but does nothing.
- **Faction rules** (such as a mutator of the form `A>B=Friendly`): a rule that RCK can't read, such as an unknown
  faction or relationship, is skipped, with an error line in `BepInEx\LogOutput.log`.
- **Custom characters:** the character creator drops traits it doesn't know when a character is opened, so saving the
  character again loses them.
- The `[RCK REQUIRED]` sign is removed whenever any version of RCK is loaded, so it can't tell a player that their
  RCK is too old.

So state the minimum version in your campaign's description, for example "Needs RCK 1.3.0 or newer", and use only
names from that version or older. A campaign made for an older RCK keeps working on newer ones, apart from major
releases.
