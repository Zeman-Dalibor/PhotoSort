# PhotoSort

Desktop application for quickly sorting photos. It displays one photo full-screen and moves it to
the `edit`, `archive`, or `delete` subfolder with a single keystroke.

Built with .NET 10 and Avalonia UI; works on Windows 10/11 and Linux.
Technical specification: [`docs/technical-specification.md`](docs/technical-specification.md).

## Run

```bash
dotnet run --project src/PhotoSort
```

## Tests

```bash
dotnet test tests/PhotoSort.Tests
```

## Controls

| Key | Action |
|-----|--------|
| `Left` / `Right` | Previous / next photo |
| `Home` / `End` | First / last photo |
| `E`, `Space` | Move to `edit` |
| `A`, `K` | Move to `archive` |
| `D`, `Delete` | Move to `delete` |
| `R` | Return to the root folder |
| `Tab` | Switch format (JPG <-> CR2) |
| `Ctrl+Z`, `Backspace` | Undo the last move |
| `O` | Choose a folder |
| `F11` | Full screen |

The same actions are also available as buttons along the edges of the window.

## How It Works

- **Format pairing** -- `IMG_0042.JPG` and `IMG_0042.CR2` form one item and are always moved
  together. The bottom bar lets you choose which format to display.
- **RAW** -- The embedded JPEG preview is read from CR2 (and NEF, ARW, DNG, ...); for Canon it is
  full resolution.
- **Disk loading** -- One thread and a priority queue. The current image always takes priority over
  preloading.
- **Cache** -- The last 10 displayed photos stay in memory, while the surrounding +/-2 are
  preloaded.
- **Nothing is deleted** -- `delete` is just another folder; `Ctrl+Z` restores the last 20 moves.

## Limitations

- **Windows 7 is not supported** -- .NET 7 and later do not run on it. Windows 10 is the minimum.
- Canon CR3 is not supported (it uses a different container than TIFF).
- Only the root folder and the three filter subfolders are scanned, not the whole tree.

---

# PhotoSort (Cesky)

Desktopová aplikace pro rychlé třídění fotografií. Zobrazí jednu fotku přes celou plochu a jedním
stiskem klávesy ji přesune do podsložky `edit`, `archive` nebo `delete`.

Postavená na .NET 10 a Avalonia UI, funguje na Windows 10/11 a na Linuxu.
Technická specifikace: [`docs/technical-specification.md`](docs/technical-specification.md).

## Instalace

Stáhni zip z [Releases](../../releases), rozbal a spusť. **Nainstalovaný .NET není potřeba** —
runtime i všechny nativní knihovny jsou uvnitř spustitelného souboru.

| Balíček | Pro |
|---------|-----|
| `PhotoSort-<verze>-windows-x64.zip` | Windows 10 / 11, 64bit |
| `PhotoSort-<verze>-windows-x86.zip` | Windows 10 / 11, 32bit |
| `PhotoSort-<verze>-linux-x64.zip` | Linux, 64bit |

Na Linuxu případně nejdřív `chmod +x PhotoSort`.

## Vývoj

```bash
dotnet run --project src/PhotoSort     # spuštění
dotnet test PhotoSort.slnx             # testy
```

## Vydání nové verze

V záložce **Actions** spusť workflow **Release** a zadej číslo verze (například `1.2`).
Workflow ověří verzi, pustí testy na Windows i Linuxu, zkompiluje všechny tři balíčky
a vytvoří GitHub Release s tagem `v1.2`.

## Ovládání

| Klávesa | Akce |
|---------|------|
| `←` / `→` | předchozí / následující fotografie |
| `Home` / `End` | první / poslední |
| `E`, `Space` | přesun do `edit` |
| `A`, `K` | přesun do `archive` |
| `D`, `Delete` | přesun do `delete` |
| `R` | zpět do kořenové složky |
| `Tab` | přepnout formát (JPG ↔ CR2) |
| `Ctrl+Z`, `Backspace` | vrátit poslední přesun |
| `O` | zvolit složku |
| `F11` | celá obrazovka |

Stejné akce jsou dostupné i jako tlačítka po okrajích okna.

## Jak to funguje

- **Párování formátů** — `IMG_0042.JPG` a `IMG_0042.CR2` tvoří jednu položku a přesouvají se
  vždy společně. Ve spodní liště lze přepnout, který formát se zobrazuje.
- **RAW** — z CR2 (a NEF, ARW, DNG, …) se čte vložený JPEG náhled; u Canonu je v plném rozlišení.
- **Načítání z disku** — jedno vlákno, prioritní fronta. Aktuální snímek má vždy přednost před
  předběžným načítáním.
- **Cache** — posledních 10 zobrazených fotografií zůstává v paměti, okolí ±2 se předběžně načítá.
- **Nic se nemaže** — `delete` je jen další složka; `Ctrl+Z` vrací posledních 20 přesunů.

## Omezení

- **Windows 7 a 8.1 nejsou podporované.** Poslední .NET, který na nich běžel, byla verze 6
  (konec podpory 11/2024). Nepomůže ani self-contained balíček — nekompatibilní je samotný
  runtime, ne způsob jeho instalace. Minimum je Windows 10 verze 1607.
- **Canon CR3** není podporován (jiný kontejner než TIFF).
- Skenuje se pouze kořenová složka a tři filtrační podsložky, ne celý strom.
- Na Linuxu je potřeba běžná desktopová instalace (`libicu`, `libfontconfig1`, `libx11-6`,
  `libice6`, `libsm6`) — v každé mainstreamové distribuci už je.
