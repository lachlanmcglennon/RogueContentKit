# RCK Pack for Streets of Rogue

RCK (Rogue Content Kit) adds the custom-content tools that Streets of Rogue campaign makers use: designer traits for
NPCs, NPC goals, mutators, factions, investigate text, container items and more. It plays campaigns and characters
made for Custom Content Utilities (CCU) unchanged; you don't need CCU.

The RCK Pack is one zip with everything needed, for the current Steam version of the game on Windows (and Steam Deck
or Linux through Proton): BepInEx 5.4.23.5, RogueLibs 4.0.0-rc.3, RogueLibsPlus and RCK.

**⚠ AI disclosure:** RCK and RogueLibsPlus were made with AI assistance. The code, the in-game trait descriptions
and the Russian and Chinese texts were written with GitHub Copilot (Claude models by Anthropic, GPT models by
OpenAI), then reviewed and tested by the author. No AI-generated art or audio is included.

Русский и 简体中文: see [below](#other-languages).

## Install (players)

1. Download `RCK-Pack-<version>.zip` from [Releases](https://github.com/lachlanmcglennon/RogueContentKit/releases).
2. In Steam, right-click Streets of Rogue and choose **Manage > Browse local files**.
3. Extract everything in the zip into that folder, next to `StreetsOfRogue.exe`. Choose **Replace** if asked.
4. Start the game. The bottom left of the main menu shows RogueLibs (`RL v4.0.0-rc.3`), `RL+ v1.0.0` and
   `RCK v1.0.0`. A red line there means something needs fixing, and it says what to do.

### Campaigns

Subscribe to a campaign on the Steam Workshop, then start it from the main menu with **Custom Content > Load Campaign**. Campaigns made for CCU
work with RCK.

### Designer mode

Designer mode lists RCK's designer traits (for NPCs) and its player traits in the character creator. It is off by
default, so players get a clean list. Turn it on to make campaigns or to edit a character that uses designer traits
(without it, the character creator drops them). Playing campaigns doesn't need it.

- In the character creator, open the Traits list and click **[RCK] Designer mode** at the top. The list changes the
  next time you open the character creator. The main menu line reads `RCK v1.0.0 (designer mode)`.
- The setting is saved as `DesignerEdition = true` under `[General]` in
  `BepInEx\config\streetsofrogue.roguecontentkit.cfg`.

### Update

Extract the new zip over the old one and choose **Replace**. Your settings are kept: the zip has no config files.

### Coming from CCU

Delete the `BepInEx\plugins\CCU` folder; RCK doesn't load while CCU is installed. If you forget, the main menu tells
you. Delete BunnyLibs too if you have it (the menu names the file). Your CCU campaigns and characters keep working.
Old RogueLibs files are replaced by the zip.

### Uninstall

- To turn all mods off, delete `winhttp.dll` from the game folder. Put it back to turn them on again.
- To remove RCK, delete `BepInEx\plugins\RCK` and `BepInEx\plugins\RogueLibsPlus`.
- To remove everything, also delete the `BepInEx` folder, `winhttp.dll`, `doorstop_config.ini` and
  `.doorstop_version`.

### Steam Deck and Linux (Proton)

The pack uses the Windows version of BepInEx, so run the Windows version of the game through Proton. In Steam, open
Streets of Rogue's **Properties** and set **Launch Options** to:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Then install as above. The native Linux and macOS versions of the game are not supported and have not been tested.

### Known limits

- Windows, or Steam Deck and Linux through Proton (see above). The native Linux and macOS versions of the game are
  not supported.
- In-game text from RCK is English only.
- Designer traits are hidden from the character creator until you turn on designer mode; the list changes the next
  time you open the character creator.
- RCK's player traits need no Chicken Nuggets: 14 of them (such as Trigger Happy and Ammo Stocker) can be offered when
  you level up, in ordinary runs too, even though the character creator hides 12 of them while designer mode is off;
  to keep them out of ordinary runs, turn them off in the Doctor's **Unlock Traits** menu at the Home Base (campaigns
  and the Daily Run ignore that switch).

### Troubleshooting

- **No RCK line on the main menu:** BepInEx didn't start. Check that `winhttp.dll` is next to `StreetsOfRogue.exe`.
  On Proton, check the launch option above.
- **A red line on the main menu:** do what it says, then restart the game.
- **Logs:** `BepInEx\LogOutput.log` in the game folder. It is rewritten each time the game starts, so copy it right
  after the problem happens.
- **Bug reports:** open an issue at `https://github.com/lachlanmcglennon/RogueContentKit/issues`. Attach `BepInEx\LogOutput.log` and
  `%USERPROFILE%\AppData\LocalLow\Streets of Rogue\Streets of Rogue\Player.log`, name the campaign (with a Workshop
  link), and say what you did and what happened.

## Other languages

<details>
<summary>Русский</summary>

### Установка (для игроков)

RCK (Rogue Content Kit) добавляет инструменты для пользовательских кампаний Streets of Rogue: трейты для NPC, цели
NPC, мутаторы, фракции, текст осмотра, предметы в контейнерах и многое другое. Кампании и персонажи, сделанные для
Custom Content Utilities (CCU), работают без изменений; сам CCU не нужен.

**⚠ ИИ.** RCK и RogueLibsPlus сделаны с помощью ИИ: код, описания трейтов в игре и тексты на русском и китайском
написаны с GitHub Copilot (модели Claude от Anthropic, модели GPT от OpenAI), затем проверены и протестированы
автором. Графики и звука, созданных ИИ, нет.

1. Скачайте `RCK-Pack-<версия>.zip` на странице [Releases](https://github.com/lachlanmcglennon/RogueContentKit/releases).
2. В Steam нажмите правой кнопкой на Streets of Rogue и выберите **Управление > Просмотреть локальные файлы**.
3. Распакуйте всё содержимое архива в эту папку, рядом с `StreetsOfRogue.exe`. Если спросят, выберите **Заменить**.
4. Запустите игру. Внизу слева в главном меню появятся строки RogueLibs (`RL v4.0.0-rc.3`), `RL+ v1.0.0` и
   `RCK v1.0.0`. Красная строка означает, что что-то нужно исправить; в ней написано, что сделать.

**Кампании.** Подпишитесь на кампанию в Мастерской Steam и запустите её из главного меню: **Пользовательский Контент > Загрузить кампанию** (в английской версии игры: Custom Content > Load Campaign). Кампании для
CCU работают с RCK.

**Режим дизайнера.** Показывает трейты RCK для NPC и для игрока в редакторе персонажей. По умолчанию выключен.
Включите его, чтобы делать кампании или редактировать персонажа с трейтами дизайнера (без него редактор их удаляет).
Для игры в кампании он не нужен. В редакторе персонажей откройте список Traits и нажмите **[RCK] Designer mode**
вверху. Список обновится при следующем открытии редактора. Настройка хранится как `DesignerEdition = true` в
`BepInEx\config\streetsofrogue.roguecontentkit.cfg`.

**Обновление.** Распакуйте новый архив поверх старого и выберите **Заменить**. Настройки сохранятся: в архиве нет
файлов настроек.

**Переход с CCU.** Удалите папку `BepInEx\plugins\CCU`: RCK не загружается, пока установлен CCU. Если забудете,
главное меню напомнит. Если у вас есть BunnyLibs, удалите и его (меню назовёт файл). Кампании и персонажи для CCU
продолжат работать.

**Удаление.** Чтобы выключить все моды, удалите `winhttp.dll` из папки игры (верните его, чтобы включить снова).
Чтобы удалить RCK, удалите `BepInEx\plugins\RCK` и `BepInEx\plugins\RogueLibsPlus`. Чтобы удалить всё, удалите ещё
папку `BepInEx`, `winhttp.dll`, `doorstop_config.ini` и `.doorstop_version`.

**Steam Deck и Linux (Proton).** Архив использует Windows-версию BepInEx, поэтому запускайте Windows-версию игры
через Proton. В **Свойствах** игры в Steam укажите в **Параметрах запуска**:
`WINEDLLOVERRIDES="winhttp=n,b" %command%`. Затем установите, как описано выше. Нативные версии игры для Linux и macOS
не поддерживаются и не проверялись.

**Ограничения.**

- Текст RCK в игре только на английском.
- Трейты дизайнера скрыты в редакторе персонажей, пока не включён режим дизайнера.
- Трейты игрока из RCK не нужно открывать за куриные наггетсы: 14 из них (например, Trigger Happy и Ammo Stocker)
  могут выпасть при повышении уровня и в обычных забегах, хотя, пока режим дизайнера выключен, редактор персонажей
  скрывает 12 из них; чтобы убрать их из обычных забегов, отключите их у Доктора на Базе в меню «Открыть особенности»
  (кампании и «Ежедневная игра» этот переключатель игнорируют).

**Если что-то не работает.** Нет строки RCK в меню: BepInEx не запустился; проверьте, что `winhttp.dll` лежит рядом
с `StreetsOfRogue.exe` (на Proton проверьте параметр запуска). Журнал: `BepInEx\LogOutput.log` в папке игры; он
перезаписывается при каждом запуске. Для сообщения об ошибке откройте issue на
`https://github.com/lachlanmcglennon/RogueContentKit/issues` и приложите `BepInEx\LogOutput.log` и
`%USERPROFILE%\AppData\LocalLow\Streets of Rogue\Streets of Rogue\Player.log`, укажите кампанию и опишите, что вы
сделали и что произошло.

</details>

<details>
<summary>简体中文</summary>

### 安装（玩家）

RCK（Rogue Content Kit）为《Streets of Rogue》（《战斗吧！流氓》）的自定义战役提供工具：NPC 设计特性、NPC 目标、
变异器、阵营、调查文本、容器物品等。为 Custom Content Utilities（CCU）制作的战役和角色可以直接使用，不需要安装 CCU。

**⚠ AI 使用说明。** RCK 和 RogueLibsPlus 借助 AI 制作：代码、游戏内的特性描述以及俄文和中文文本由 GitHub Copilot
（Anthropic 的 Claude 模型、OpenAI 的 GPT 模型）编写，并经作者审核和测试。不包含 AI 生成的美术或音频。

1. 从 [Releases](https://github.com/lachlanmcglennon/RogueContentKit/releases) 下载 `RCK-Pack-<版本>.zip`。
2. 在 Steam 中右键点击 Streets of Rogue，选择 **管理 > 浏览本地文件**。
3. 把压缩包里的所有内容解压到这个文件夹，也就是 `StreetsOfRogue.exe` 所在的位置。如果询问，选择 **替换**。
4. 启动游戏。主菜单左下角会显示 RogueLibs（`RL v4.0.0-rc.3`）、`RL+ v1.0.0` 和 `RCK v1.0.0`。如果出现红色的行，
   说明有问题需要处理，按照那一行的提示操作即可。

**战役。** 在 Steam 创意工坊订阅战役，然后在主菜单选择 **自定义内容 > 加载活动**（英文界面为 Custom Content > Load Campaign）开始。为 CCU 制作的战役可以在 RCK 下运行。

**设计者模式。** 在角色创建器中显示 RCK 的 NPC 设计特性和玩家特性。默认关闭。制作战役或编辑使用设计特性的角色时请打开它
（关闭时，角色创建器会删除这些特性）。只玩战役不需要它。在角色创建器中打开 Traits 列表，点击顶部的
**[RCK] Designer mode**。下次打开角色创建器时列表会更新。该设置保存在
`BepInEx\config\streetsofrogue.roguecontentkit.cfg` 中，为 `DesignerEdition = true`。

**更新。** 把新的压缩包解压到旧文件上，选择 **替换**。你的设置会保留：压缩包里没有配置文件。

**从 CCU 迁移。** 删除 `BepInEx\plugins\CCU` 文件夹：安装了 CCU 时 RCK 不会加载。如果忘了，主菜单会提示。如果装有
BunnyLibs，也请删除（菜单会显示文件名）。为 CCU 制作的战役和角色可以继续使用。

**卸载。** 要关闭所有模组，删除游戏文件夹中的 `winhttp.dll`（放回去即可重新启用）。要删除 RCK，删除
`BepInEx\plugins\RCK` 和 `BepInEx\plugins\RogueLibsPlus`。要删除全部内容，再删除 `BepInEx` 文件夹、`winhttp.dll`、
`doorstop_config.ini` 和 `.doorstop_version`。

**Steam Deck 和 Linux（Proton）。** 本包使用 Windows 版 BepInEx，因此请通过 Proton 运行 Windows 版游戏。在 Steam 中打开
游戏的 **属性**，在 **启动选项** 中填写：`WINEDLLOVERRIDES="winhttp=n,b" %command%`，然后按上面的步骤安装。
游戏的原生 Linux 和 macOS 版本不受支持，也未经测试。

**已知限制。**

- RCK 的游戏内文本只有英文。
- 在打开设计者模式之前，角色创建器中不显示设计特性。
- RCK 的玩家特性无需用鸡块解锁：其中 14 个（如 Trigger Happy 和 Ammo Stocker）在普通游戏中升级时也可能出现，尽管关闭设计者模式时角色创建器会隐藏其中 12 个；
  如不想在普通游戏中遇到它们，请在大本营找医生，在“解锁技能”菜单中将其关闭（战役和日常任务会忽略这个开关）。

**故障排除。** 主菜单没有 RCK 行：BepInEx 没有启动，请确认 `winhttp.dll` 与 `StreetsOfRogue.exe` 在同一文件夹
（Proton 下请检查启动选项）。日志：游戏文件夹中的 `BepInEx\LogOutput.log`，每次启动游戏都会重写。报告错误时，请在
`https://github.com/lachlanmcglennon/RogueContentKit/issues` 提交 issue，附上 `BepInEx\LogOutput.log` 和
`%USERPROFILE%\AppData\LocalLow\Streets of Rogue\Streets of Rogue\Player.log`，写明战役名称，并说明你做了什么、发生了什么。

</details>

## Features

- What each RCK system does: [Appearance](RCK/Systems/Appearance/FEATURES.md),
  [Behavior](RCK/Systems/Behavior/FEATURES.md), [Campaign](RCK/Systems/Campaign/FEATURES.md),
  [Combat](RCK/Systems/Combat/FEATURES.md), [Interactions](RCK/Systems/Interactions/FEATURES.md),
  [Loadout](RCK/Systems/Loadout/FEATURES.md), [Merchants](RCK/Systems/Merchants/FEATURES.md),
  [Objects](RCK/Systems/Objects/FEATURES.md), [PlayerTraits](RCK/Systems/PlayerTraits/FEATURES.md),
  [Social](RCK/Systems/Social/FEATURES.md).
- [Publishing a campaign that needs RCK](docs/publishing.md) (Steam Workshop and GameBanana).
- [RogueLibsPlus](docs/roguelibsplus.md): the fixes and APIs RogueLibsPlus adds to RogueLibs.
- [Names kept from CCU](docs/legacy-ccu-vocabulary.md) and the full [CCU interface](docs/ccu-interface.md).

## Credits and licence

- RCK and RogueLibsPlus: RCK contributors, MIT licence (`LICENSE`, `RCK/LICENSE`, `RogueLibsPlus/LICENSE`).
- RogueLibs by Chasmical (Abbysssal); the v4.0.0-rc.3 release by Dzhake. MIT. Shipped unmodified.
- BepInEx by the BepInEx team, with UnityDoorstop, HarmonyX, MonoMod and Mono.Cecil. Shipped unmodified.
- Custom Content Utilities (CCU) by Freiling87 (Ted Bunny) is the original custom-content mod and the idea RCK
  follows. RCK is written from scratch and reuses only the names that campaigns store.

Licences and sources for everything in the pack: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Building from source

You need Windows, the .NET SDK (7 or later), Python 3 and Streets of Rogue installed from Steam. Close the game first.

```powershell
# Reference assemblies from your game install (see RefBuilder below)
$game = 'C:\Program Files (x86)\Steam\steamapps\common\Streets of Rogue'
New-Item -ItemType Directory -Force .ref\static | Out-Null
Copy-Item "$game\StreetsOfRogue_Data\Managed\*.dll" .ref\static -Force
Copy-Item "$game\BepInEx\core\*.dll" .ref\static -Force
Remove-Item .ref\static\Assembly-CSharp.dll
dotnet run -c Release --project tools\RefBuilder -- "$game\StreetsOfRogue_Data\Managed\Assembly-CSharp.dll" .ref\Assembly-CSharp.dll --publicize
# A C# decompile of the game for the game-string check in verify-ccu (ilspycmd: dotnet tool install -g ilspycmd)
ilspycmd -p -o .ref\decomp "$game\StreetsOfRogue_Data\Managed\Assembly-CSharp.dll"

powershell -File tools\get-roguelibs.ps1       # Dzhake's rc.3 into .ref\roguelibs (SHA256-checked)
powershell -File RCK\deploy.ps1 -BuildOnly     # builds RogueLibsPlus, RCK and every RCK module
powershell -File tools\verify-ccu.ps1          # checks every Harmony patch target against the game; 0 ERRORs expected
powershell -File tools\package-release.ps1     # builds release\RCK-Pack-<version>.zip
```

`.ref` holds game files and is never committed.

- `RogueLibsPlus/`: the RogueLibs add-on (GUID `streetsofrogue.roguelibsplus`).
- `RCK/Core` builds `RCK.dll`, the plugin (GUID `streetsofrogue.roguecontentkit`). `RCK/Systems/<X>` builds
  `RCK.<X>.dll`, one per system. `RCK/Core/Generated` is generated from `docs/ccu-interface.json` and
  `RCK/Core/Data/trait-descriptions.json` by the scripts in `tools/ccu-codegen`.
- `tools/RefBuilder`: builds `.ref/Assembly-CSharp.dll` with RogueLibs' fields injected and all members public, for
  compiling. `tools/PatchVerifier`, `tools/ButtonCheck` and `tools/check-private-access.ps1` are the checks
  `verify-ccu.ps1` runs.
