# GroupsMaker

Command line tool that finds bursts and duplicates in a folder of photos and moves each cluster
into its own sub-folder, so the only thing left to do is pick the best shot of every group.

```
D:\Photos\                      D:\Photos\
  IMG_1001.JPG                    Groups\
  IMG_1001.CR2                      001_IMG_1001_5x\   IMG_1001.JPG + .CR2, IMG_1002 … IMG_1005
  IMG_1002.JPG        ──▶           002_IMG_2001_3x\   IMG_2001 … IMG_2003
  …                                 003_IMG_3001_2x\   IMG_3001, IMG_9001  (duplicates)
  IMG_3002.JPG                      groups-manifest.json
                                  IMG_3002.JPG         (no partner, left alone)
```

## Run

```bash
dotnet run --project GroupsMaker -- "D:\Photos"
dotnet run --project GroupsMaker -- "D:\Photos" --dry-run
dotnet run --project GroupsMaker -- "D:\Photos" --undo
```

Standalone executable, no .NET needed on the target machine:

```bash
dotnet publish GroupsMaker --configuration Release --runtime win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Options

| Option | Default | Meaning |
|--------|---------|---------|
| `-o`, `--output <name>` | `Groups` | Name of the folder created inside the input folder. |
| `-t`, `--time-gap <sec>` | `10` | Longest pause that still counts as the same burst. |
| `-s`, `--similarity <n>` | `10` | Hash distance allowed inside a burst, 0–64. Lower is stricter. |
| `-d`, `--duplicate <n>` | `4` | Hash distance that groups photos no matter how far apart in time. `-1` disables the rule. |
| `-m`, `--min-size <n>` | `2` | Smallest group that gets its own folder. |
| `-r`, `--recursive` | off | Also scan sub-folders. |
| `-n`, `--dry-run` | off | Print the plan, move nothing. |
| `-j`, `--threads <n>` | CPU count | Files fingerprinted in parallel. |
| `-u`, `--undo` | — | Move everything back using the manifest. |

Exit codes: `0` success, `1` error, `2` bad usage.

## How It Works

1. **Scan** — supported files are listed and grouped by name, so `IMG_0042.JPG` and `IMG_0042.CR2`
   count as one photograph and always move together. Formats and RAW preview extraction are shared
   with the desktop app (`SupportedFormats`, `TiffPreviewExtractor` are linked, not copied).
2. **Fingerprint** — every photo is decoded to 128 px (RAW through its embedded JPEG preview) and
   reduced to a 64-bit difference hash. Capture time comes from EXIF `DateTimeOriginal` including
   sub-seconds, falling back to the file timestamp. This step runs on all cores.
3. **Group** — two photos land in the same group when they were taken within `--time-gap` of each
   other *and* their hashes differ by at most `--similarity` bits, or when their hashes differ by
   at most `--duplicate` bits regardless of time. The relation is transitive, so a long burst stays
   one group even though its first and last frame no longer resemble each other.
4. **Move** — one folder per group, named `<ordinal>_<first photo>_<count>x`. Name collisions get a
   ` (n)` suffix applied to the whole JPG+CR2 pair at once. Every move is recorded in
   `Groups/groups-manifest.json`.

Nothing is ever deleted or overwritten, and `--undo` restores the original layout from the manifest.

## Tuning

- **Too much lumped together** — lower `--similarity` (try 6) or `--time-gap` (try 3).
- **A burst split into several folders** — raise `--similarity` (try 14) or `--time-gap`.
- **Only exact duplicates wanted** — `--time-gap 0 --duplicate 2`.

## Limitations

- Hashes are computed without applying EXIF orientation. Photos in one burst share their
  orientation, so this does not affect grouping in practice.
- The duplicate rule compares every pair, which is fine up to a few tens of thousands of photos in
  one run.
- Canon CR3 is not supported, for the same reason as in the desktop app.

---

# GroupsMaker (Česky)

Nástroj pro příkazovou řádku, který ve složce najde dávky (bursty) a duplicity a každý shluk
přesune do vlastní podsložky. Zbývá už jen vybrat nejlepší snímek z každé skupiny.

## Spuštění

```bash
dotnet run --project GroupsMaker -- "D:\Fotky"              # rozdělí do složek
dotnet run --project GroupsMaker -- "D:\Fotky" --dry-run    # jen vypíše, co by udělal
dotnet run --project GroupsMaker -- "D:\Fotky" --undo       # vrátí vše zpátky
```

Samostatné `.exe`, které nepotřebuje nainstalovaný .NET:

```bash
dotnet publish GroupsMaker --configuration Release --runtime win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Přepínače

| Přepínač | Výchozí | Význam |
|----------|---------|--------|
| `-o`, `--output <název>` | `Groups` | Název složky vytvořené ve vstupní složce. |
| `-t`, `--time-gap <s>` | `10` | Nejdelší pauza, která ještě patří do jedné dávky. |
| `-s`, `--similarity <n>` | `10` | Povolená vzdálenost hashů uvnitř dávky, 0–64. Nižší = přísnější. |
| `-d`, `--duplicate <n>` | `4` | Vzdálenost hashů, při které se fotky spojí bez ohledu na čas. `-1` pravidlo vypne. |
| `-m`, `--min-size <n>` | `2` | Nejmenší skupina, která dostane vlastní složku. |
| `-r`, `--recursive` | vypnuto | Prohledat i podsložky. |
| `-n`, `--dry-run` | vypnuto | Jen vypsat plán, nic nepřesouvat. |
| `-j`, `--threads <n>` | počet jader | Kolik souborů se zpracovává paralelně. |
| `-u`, `--undo` | — | Vrátit všechny přesuny podle manifestu. |

Návratové kódy: `0` úspěch, `1` chyba, `2` špatné použití.

## Jak to funguje

1. **Sken** — soubory se seskupí podle názvu, takže `IMG_0042.JPG` a `IMG_0042.CR2` tvoří jednu
   fotografii a přesouvají se společně. Seznam formátů i extrakci RAW náhledů sdílí s desktopovou
   aplikací (soubory `SupportedFormats` a `TiffPreviewExtractor` jsou nalinkované, ne zkopírované).
2. **Otisk** — každá fotka se dekóduje na 128 px (RAW přes vložený JPEG náhled) a převede na
   64bitový difference hash. Čas pořízení se bere z EXIF `DateTimeOriginal` včetně setin sekundy,
   jinak z časového razítka souboru. Běží na všech jádrech.
3. **Skupiny** — dvě fotky patří k sobě, když byly pořízeny do `--time-gap` od sebe *a zároveň* se
   jejich hashe liší nejvýš o `--similarity` bitů, nebo když se liší nejvýš o `--duplicate` bitů
   bez ohledu na čas. Vztah je tranzitivní, takže dlouhá dávka zůstane jednou skupinou, i když se
   její první a poslední snímek už nepodobají.
4. **Přesun** — jedna složka na skupinu, pojmenovaná `<pořadí>_<první fotka>_<počet>x`. Kolize
   názvů řeší přípona ` (n)`, aplikovaná najednou na celý pár JPG+CR2. Každý přesun se zapíše do
   `Groups/groups-manifest.json`.

Nic se nikdy nemaže ani nepřepisuje a `--undo` obnoví původní rozložení podle manifestu.

## Ladění výsledku

- **Slepilo se toho moc** — snižte `--similarity` (zkuste 6) nebo `--time-gap` (zkuste 3).
- **Jedna dávka se rozpadla do více složek** — zvyšte `--similarity` (zkuste 14) nebo `--time-gap`.
- **Chci jen přesné duplicity** — `--time-gap 0 --duplicate 2`.

## Omezení

- Hash se počítá bez aplikace EXIF orientace. Fotky v jedné dávce mají orientaci stejnou, takže to
  na výsledek nemá vliv.
- Pravidlo pro duplicity porovnává každou dvojici; do řádu desítek tisíc fotek v jednom běhu je to
  bez problémů.
- Canon CR3 není podporován, ze stejného důvodu jako v desktopové aplikaci.
