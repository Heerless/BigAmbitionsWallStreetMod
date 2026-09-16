# Big Ambitions: Wall Street

A player-owned trading floor for [Big Ambitions](https://www.bigambitionsgame.com/).

Rent an office, fill it with desks, headhunt Brokers, and commit capital. Each hour the
staffed desks put that capital to work and return a profit or a loss, booked to the
business like any other revenue. No stock, no deliveries, no customers — the product is
the trade.

## How it plays

**Desks are capacity.** One staffed desk works **$1,000,000**. Commit beyond
`desks × $1M` and the remainder sits idle earning nothing, so a wider floor is what lets
you deploy more capital — and costs a wage bill to keep.

**Brokers are headhunter-only.** They do not appear at the recruitment agencies. Staffing
a floor means paying a headhunter, which is deliberate: this is an endgame business.

**Capital is real.** Committing moves money out of your balance and into the floor.
Withdrawing brings it back.

**Returns taper.** Past $10M, profit flattens — ten times the capital earns roughly five
times as much, not ten. Scaling up always pays, never proportionally.

**The market has moods.** A regime drifts daily between crisis, choppy, normal and bull,
shared across your floors. Crises are survivable, not cosmetic.

**Compliance is insurance.** Retain an officer for 2% of profit and desks cannot blow up.
Dismiss them and each desk-hour carries a small chance of losing 15–40% of its slice. The
fee is charged only on winning hours, so it can never deepen a loss.

## Building

Requires the [official Big Ambitions modding SDK](https://github.com/hovgaardgames/bigambitions)
and Unity **2022.3.62f2** with the macOS Build Support module.

1. Clone the SDK and open it in Unity, following its welcome window to import the game DLLs
2. Copy `Assets/Mods/WallStreet/` into the SDK project's `Assets/Mods/`
3. Copy `Assets/Editor/ModBuilder/WallStreet*.cs` alongside the SDK's Mod Builder
4. **Big Ambitions → Mod Builder → Build & Install**

### Headless

`WallStreetCI` drives the whole loop from the command line, which is considerably faster
than clicking through the editor:

```bash
Unity.exe -batchmode -nographics -projectPath <sdk> \
  -executeMethod BAModTemplate.Editor.WallStreetCI.BuildAndInstall -logFile build.log
```

`WallStreetApiDump.Dump` writes the game's real member lists to `api-dump.txt`, so the mod
can be written against actual types rather than guessed member names.
`WallStreetIconMaker.Create` regenerates the business icon.

## Notes on the internals

A few things about Big Ambitions that cost real time to learn, recorded so they don't have
to be learned twice:

- **Never write `suitableSkills` onto an item whose array is empty.** Empty means furniture;
  non-empty means workstation. Writing to plain desks promotes them into workstations in
  every office you own and breaks schedules empire-wide.
- **Never call `SkillHelper.OnSkillDataLoaded`.** It replaces the roster with whatever list
  it is handed. A partial list drops every other skill, which makes `GameManager.NewDay()`
  throw and silently zeroes every income statement in the save. Add to the
  `SkillHelper.Skills` dictionary in place.
- **Register mod items at city load, not initialisation.** The item catalogue reloads after
  init and drops anything added earlier.
- **`EconoViewBusinessDetails.LoadSales` dereferences `ItemsGetter.GetByName(...)` with no
  null check.** A Sales row naming an unregistered item throws and collapses the whole
  statement into one "Undefined" line.
- **`orderHistory` is the source of truth** for the income statement and the inventory
  screens. Write there; everything else derives itself.
- **The clock is `TimeHelper.CurrentHour`.** There is no static hourly event anywhere.
- **`JsonUtility` will serialise a container to `{}` and report success.** Persistence here
  is written by hand and read back to verify.

## Licence

The mod source is free to use. The Big Ambitions modding SDK it builds against belongs to
Hovgaard Games and is not included here.
