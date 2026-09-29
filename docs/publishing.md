# Publishing a campaign that needs RCK

How to publish a Streets of Rogue campaign that uses RCK traits, goals or mutators, so players know to install the
RCK Pack. Campaigns made for CCU need the same treatment: CCU players now use RCK.

Download link for players: `https://github.com/lachlanmcglennon/RogueContentKit/releases`

## Steam Workshop

### Title

Put `[RCK]` at the start of the title, e.g. `[RCK] Night of the Crepes`. Players can then search for RCK campaigns.

### Description

Paste one of these at the top of the Workshop description. Use all three if your campaign has Russian or Chinese
players. Keep the AI disclosure line: it tells players how the mod they are asked to install was made.

```
[h2]Requires RCK[/h2]
This campaign needs the RCK Pack (Rogue Content Kit). CCU campaigns also run on RCK.
[list]
[*]Download [b]RCK-Pack[/b] from [url=https://github.com/lachlanmcglennon/RogueContentKit/releases]the RCK releases page[/url].
[*]Extract it into the game folder (Steam: Manage > Browse local files), next to StreetsOfRogue.exe.
[*]Start the game: the main menu shows "RCK v1.0.0" at the bottom left.
[/list]
[b]AI disclosure:[/b] RCK and RogueLibsPlus were made with AI assistance: GitHub Copilot, with Claude models by Anthropic and GPT models by OpenAI. No AI-generated art or audio is included.
```

```
[h2]Нужен RCK[/h2]
Для этой кампании нужен RCK Pack (Rogue Content Kit). Кампании для CCU тоже работают с RCK.
[list]
[*]Скачайте [b]RCK-Pack[/b] на [url=https://github.com/lachlanmcglennon/RogueContentKit/releases]странице релизов RCK[/url].
[*]Распакуйте его в папку игры (Steam: Управление > Просмотреть локальные файлы), рядом с StreetsOfRogue.exe.
[*]Запустите игру: внизу слева в главном меню появится «RCK v1.0.0».
[/list]
[b]Использование ИИ:[/b] RCK и RogueLibsPlus сделаны с помощью ИИ: GitHub Copilot, модели Claude от Anthropic и модели GPT от OpenAI. Графики и звука, созданных ИИ, нет.
```

```
[h2]需要 RCK[/h2]
本战役需要 RCK Pack（Rogue Content Kit）。为 CCU 制作的战役也可以在 RCK 下运行。
[list]
[*]从 [url=https://github.com/lachlanmcglennon/RogueContentKit/releases]RCK 发布页面[/url] 下载 [b]RCK-Pack[/b]。
[*]把它解压到游戏文件夹（Steam：管理 > 浏览本地文件），也就是 StreetsOfRogue.exe 所在的位置。
[*]启动游戏：主菜单左下角会显示“RCK v1.0.0”。
[/list]
[b]AI 使用说明：[/b]RCK 和 RogueLibsPlus 借助 AI 制作：GitHub Copilot，使用 Anthropic 的 Claude 模型和 OpenAI 的 GPT 模型。不包含 AI 生成的美术或音频。
```

### The missing-RCK sign

Players who haven't installed RCK should find out in the game, not by a broken level. Place a vanilla **Sign** near
the player's spawn in level 1 and give it text that starts with the tag `[RCK REQUIRED]`, followed by the message
in English, Russian and Chinese, e.g.:

```
[RCK REQUIRED] This campaign needs RCK: https://github.com/lachlanmcglennon/RogueContentKit/releases
Нужен RCK: https://github.com/lachlanmcglennon/RogueContentKit/releases
需要 RCK：https://github.com/lachlanmcglennon/RogueContentKit/releases
```

- The text goes in the sign's text field (its `extraVarString`). The tag must be the very first characters, exactly
  `[RCK REQUIRED]` (capitals, no space before it).
- With RCK installed, every sign whose text starts with the tag is removed when a level starts, in every level, for
  the host and every client, before it can show on screen. The log gets one line per level
  (`Missing-RCK signs: removed N at level start.`).
- Without RCK, the sign stays, so the player reads the message.
- Mods can read the tag as `RCK.Rck.MissingRckSignTag`.

## Cover badge

A "Requires RCK" badge, and a tool to stamp it on a Workshop cover image, are coming. Until then, the `[RCK]` title
tag is enough.

<!-- Placeholder: badge image, stamp tool and instructions go here. -->

## GameBanana

Upload the same `RCK-Pack-<version>.zip` from the releases page; don't repackage it.

- **Title:** `RCK Pack (Rogue Content Kit)`.
- **AI disclosure:** GameBanana requires it, and it must be prominent: put it at the very top of the description,
  in bold, and tick the page's AI-content option if the submission form has one. Use the release notes' first
  paragraph, which names the tools and their makers:

  > **⚠ AI disclosure:** RCK and RogueLibsPlus were made with AI assistance. The code, the in-game trait
  > descriptions and the Russian and Chinese texts were written with GitHub Copilot (Claude models by Anthropic, GPT
  > models by OpenAI), then reviewed and tested by the author. No AI-generated art or audio is included.

- **Description:** the AI disclosure first, then what it is (custom-content tools for campaigns; CCU campaigns
  work), the install steps from the README, and a link to the releases page for updates.
- **Screenshots:** the main menu with the version lines at the bottom left, the character creator with designer mode
  on, and a campaign using RCK traits.
- **Credits:**
  - RCK contributors: RCK and RogueLibsPlus.
  - Chasmical (Abbysssal): RogueLibs. Dzhake: the RogueLibs v4.0.0-rc.3 release.
  - The BepInEx team: BepInEx (with UnityDoorstop, HarmonyX, MonoMod and Mono.Cecil).
  - Freiling87 (Ted Bunny): Custom Content Utilities (CCU), the original custom-content mod and the idea RCK follows.
    RCK is written from scratch and reuses only the names that campaigns store.
- **Licence:** MIT for RCK and RogueLibsPlus; the bundled components keep their own licences (RogueLibs, BepInEx,
  HarmonyX, Harmony, MonoMod and Mono.Cecil are MIT; UnityDoorstop is LGPL-2.1). See `THIRD-PARTY-NOTICES.md`, which is
  also in the zip as `RCK-licenses\THIRD-PARTY-NOTICES.md`.
- **Campaign pages** on GameBanana should list the RCK Pack as a requirement and link to it.
