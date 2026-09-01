# PhotoSort — Technická a implementační specifikace

Verze: 1.0
Cílová platforma: .NET 10 (`net10.0`), Avalonia UI 11.3
Podporované OS: Windows 10, Windows 11, Linux (X11/Wayland)

---

## 1. Účel aplikace

PhotoSort je desktopová aplikace pro rychlé **třídění (culling) fotografií**. Uživatel zvolí hlavní
složku, aplikace mu postupně zobrazuje jednotlivé fotografie přes téměř celou obrazovku a on je
jedním stiskem klávesy nebo kliknutím zařadí do jedné ze tří podsložek:

| Kategorie | Podsložka | Klávesy |
|-----------|-----------|---------|
| Edit      | `edit`    | `E`, `Space` |
| Archive   | `archive` | `A`, `K` |
| Delete    | `delete`  | `Delete`, `D` |

Po zařazení se soubory fyzicky přesunou a aplikace automaticky zobrazí následující fotografii.

---

## 2. Rozsah (scope)

### 2.1 Ve scope

- Výběr hlavní složky přes systémový dialog.
- Volitelné zahrnutí fotografií, které už v podsložkách `edit` / `archive` / `delete` leží.
- Zobrazení jedné fotografie „na celou plochu“ s ovládacími prvky po okrajích.
- Navigace vpřed / vzad (tlačítka i klávesnice).
- Zařazení do kategorie (tlačítka i klávesnice) s okamžitým fyzickým přesunem souborů.
- Vizuální příznak aktuální kategorie fotografie.
- Miniatury předchozí a následující fotografie po stranách.
- Seskupení souborů se stejným názvem a různou příponou (`IMG_1234.JPG` + `IMG_1234.CR2`)
  do jedné položky; přesun pracuje se skupinou jako s celkem.
- Přepínání zobrazeného formátu (varianty) v rámci skupiny.
- Vrácení poslední operace (undo).
- Paměťová cache posledních 10 zobrazených fotografií + předběžné načítání 2 vzad / 2 vpřed.
- Načítání z disku výhradně jedním vláknem přes prioritní frontu.

### 2.2 Mimo scope

- Editace fotografií, rotace ukládaná na disk, hodnocení hvězdičkami, tagy.
- Rekurzivní procházení celého stromu složek (skenuje se pouze kořen + tři filtrační podsložky).
- Plná demozaikace RAW (viz §6.3 — používá se vložený JPEG náhled).
- Trvalá databáze / stav mezi spuštěními.

---

## 3. Podporované formáty

### 3.1 Rastrové formáty dekódované přímo (SkiaSharp)

`.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.webp`, `.tif`, `.tiff`

### 3.2 RAW formáty (přes vložený JPEG náhled)

`.cr2` (povinné), dále `.nef`, `.arw`, `.dng`, `.orf`, `.rw2`, `.pef`, `.raf`, `.srw`

Všechny uvedené jsou TIFF-kontejnery (kromě `.raf`, kde se náhled hledá heuristicky), takže je
pokrývá jeden společný extraktor náhledů (§6.3).

> **Poznámka:** Canon `.cr3` **není** podporován — jde o ISO-BMFF kontejner s jinou strukturou.

### 3.3 Seskupení variant

Položka (`PhotoItem`) = množina souborů ve stejné složce se shodným názvem bez přípony
(porovnání case-insensitive, `OrdinalIgnoreCase`).

- `IMG_0042.JPG` + `IMG_0042.CR2` → jedna položka se dvěma variantami.
- Výchozí zobrazovaná varianta: preferuje se rastrový formát (rychlejší dekódování) podle pořadí
  z §3.1, teprve pak RAW.
- Přesun / undo pracuje **vždy se všemi variantami skupiny**.

---

## 4. Architektura

### 4.1 Vrstvy

```
┌──────────────────────────────────────────────────────────┐
│ Views (Avalonia XAML)                                    │
│   MainWindow.axaml — layout, key bindings, data binding  │
├──────────────────────────────────────────────────────────┤
│ ViewModels (CommunityToolkit.Mvvm)                       │
│   MainWindowViewModel, PhotoItemViewModel                │
├──────────────────────────────────────────────────────────┤
│ Services (čistý C#, bez závislosti na UI kromě Bitmap)   │
│   PhotoLibrary · PhotoScanner · PhotoFileMover           │
│   SequentialImageLoader · ImageCache · SkiaImageDecoder  │
│   TiffPreviewExtractor · FileSystem                      │
├──────────────────────────────────────────────────────────┤
│ Models (immutable záznamy)                               │
│   PhotoItem · PhotoVariant · PhotoCategory · LoadRequest │
└──────────────────────────────────────────────────────────┘
```

Každá služba řeší **právě jednu odpovědnost**. Skládání závislostí je explicitní v `App.axaml.cs`
(konstruktorová injekce, bez DI kontejneru — aplikace má jednu obrazovku).

### 4.2 Struktura repozitáře

```
PhotoSort.slnx
docs/technical-specification.md
src/PhotoSort/
  Program.cs
  App.axaml, App.axaml.cs          — kompozice závislostí
  app.manifest                     — per-monitor DPI awareness (Windows)
  Models/
    PhotoCategory.cs               — enum None/Edit/Archive/Delete
    PhotoVariant.cs                — jeden soubor
    PhotoItem.cs                   — skupina souborů = jedna fotografie
    ImageRequest.cs                — LoadPriority, ImageSize
  Services/
    SupportedFormats.cs            — seznam přípon + pořadí preference
    CategoryFolders.cs             — mapování kategorie ↔ název složky
    NaturalStringComparer.cs       — IMG_2 před IMG_10
    PhotoScanner.cs                — sken složky → PhotoItem
    PhotoFileMover.cs              — přesun skupiny + řešení kolizí
    MoveRecord.cs                  — data pro undo
    PhotoLibrary.cs                — kurzor v seznamu, kategorizace, undo
    IImageDecoder.cs               — hranice pro testovatelnost loaderu
    SkiaImageDecoder.cs            — SKCodec → Avalonia Bitmap
    DecodedImage.cs                — výsledek dekódování nebo chyba
    TiffPreviewExtractor.cs        — JPEG náhled z RAW
    SequentialImageLoader.cs       — jedno vlákno + prioritní fronta
    LruCache.cs                    — obecná LRU cache
    ImageProvider.cs               — cache + loader za jedním rozhraním
    IFolderPicker.cs               — abstrakce dialogu složky
    StorageProviderFolderPicker.cs — implementace přes Avalonia StorageProvider
  ViewModels/
    MainWindowViewModel.cs
    VariantOption.cs
  Views/
    MainWindow.axaml, MainWindow.axaml.cs
  Converters/
    CategoryToBrushConverter.cs
    CategoryEqualsConverter.cs
tests/PhotoSort.Tests/
  TempFolder.cs                    — dočasná složka pro testy nad reálným FS
  TestAppBuilder.cs                — headless Avalonia pro testy
  PhotoScannerTests.cs
  PhotoFileMoverTests.cs
  PhotoLibraryTests.cs
  LruCacheTests.cs
  TiffPreviewExtractorTests.cs
  ImagePipelineTests.cs
  MainWindowViewModelTests.cs
  MainWindowRenderTests.cs
```

---

## 5. Datový model

```csharp
enum PhotoCategory { None, Edit, Archive, Delete }

sealed record PhotoVariant(string FullPath, string Extension, long SizeBytes)
{
    bool IsRaw { get; }          // podle SupportedFormats
}

sealed class PhotoItem
{
    string Key { get; }                       // název bez přípony (lowercase)
    string DisplayName { get; }               // název bez přípony (originální)
    PhotoCategory Category { get; }           // odvozeno z aktuálního umístění
    IReadOnlyList<PhotoVariant> Variants { get; }
    int SelectedVariantIndex { get; }
    PhotoVariant SelectedVariant { get; }
}
```

`PhotoItem` je mutovatelný jen ve dvou bodech: po přesunu (`Category` + cesty variant) a při změně
vybrané varianty. Obojí přes explicitní metody, ne přes veřejné settery.

**Identita pro cache:** `CacheKey = SelectedVariant.FullPath + "|" + targetSize`. Po přesunu se
cache položky pro staré cesty invalidují přemapováním klíče (viz §7.3).

---

## 6. Načítání obrázků

### 6.1 Řetězec dekódování

```
soubor → [RAW?] → TiffPreviewExtractor → byte[] JPEG náhledu
       → [rastr?] → FileStream
                       ↓
              SkiaImageDecoder (SKCodec)
                       ↓  sampleSize (mocnina 2) podle cílové šířky
                 SKBitmap (BGRA8888)
                       ↓  aplikace EXIF orientace
              Avalonia.Media.Imaging.Bitmap
```

- **Downscaling při dekódování:** `SKCodec.GetScaledDimensions` / `SKSampleSize` — nikdy se
  nedekóduje plné rozlišení do paměti. Cílové rozměry:
  - hlavní zobrazení: max hrana **2560 px**,
  - miniatura: max hrana **240 px**.
- **Orientace:** primárně `SKCodec.EncodedOrigin`. Pokud je `TopLeft` a jde o RAW, použije se
  EXIF `Orientation` z `MetadataExtractor` nad původním RAW souborem.
- Výsledek se převede na `WriteableBitmap` (`PixelFormat.Bgra8888`, `AlphaFormat.Premul`).

### 6.2 Chybové stavy

Nepodařené dekódování nesmí shodit aplikaci. Loader vrátí `ImageLoadResult.Failure(message)`
a UI zobrazí placeholder s textem chyby; navigace zůstává funkční.

### 6.3 Extrakce náhledu z RAW (`TiffPreviewExtractor`)

RAW soubory jsou TIFF kontejnery. Extraktor:

1. Přečte hlavičku (`II*\0` / `MM\0*`), zjistí endianitu a offset IFD0.
2. Iterativně projde všechny IFD v řetězu `NextIFDOffset` a rekurzivně `SubIFDs` (tag `0x014A`),
   maximálně 32 IFD (ochrana proti smyčce).
3. V každém IFD hledá kandidáty na JPEG blob:
   - `JPEGInterchangeFormat` (`0x0201`) + `JPEGInterchangeFormatLength` (`0x0202`)
   - `StripOffsets` (`0x0111`) + `StripByteCounts` (`0x0117`) pokud `Compression` (`0x0103`) ∈ {6, 7}
4. Vybere **největší** kandidát, jehož prvních 2 bajtů je `FF D8` (JPEG SOI).
5. Fallback: skenování souboru na dvojici `FFD8…FFD9` (pokrývá Fujifilm RAF).

U Canon CR2 tento postup vrací JPEG v plném rozlišení uložený v IFD0 — což je přesně to, co
potřebuje culling nástroj.

### 6.4 Sekvenční loader (`SequentialImageLoader`)

**Požadavek:** z disku se čte **jen jedním vláknem**, požadavky jsou ve frontě.

- Jedno dedikované vlákno (`Thread`, `IsBackground = true`, `Name = "photo-io"`).
- Fronta = `PriorityQueue`-like struktura chráněná `lock` + `ManualResetEventSlim`:
  - `LoadPriority.Immediate` — právě zobrazovaná fotografie,
  - `LoadPriority.Thumbnail` — miniatury sousedů,
  - `LoadPriority.Prefetch` — ±1, ±2 v plné velikosti.
- Uvnitř jedné priority platí LIFO (poslední požadavek uživatele je nejrelevantnější).
- Duplicitní požadavek na stejný `CacheKey` se sloučí (vrací se stejný `Task`).
- Při změně aktuálního indexu se **zahodí** všechny dosud nespuštěné požadavky priority
  `Prefetch`/`Thumbnail`, které už nejsou v okně ±2. Rozpracovaný požadavek se nepřerušuje
  (dekódování jednoho snímku je krátké).
- Výsledek se předává do UI přes `Dispatcher.UIThread.Post`.

### 6.5 Prefetch strategie

Při přechodu na index `i` se zařadí v tomto pořadí:

1. `i` — `Immediate`, plná velikost
2. `i-1`, `i+1` — `Thumbnail`
3. `i+1`, `i-1` — `Prefetch`, plná velikost
4. `i+2`, `i-2` — `Prefetch`, plná velikost

Požadavek se zařadí jen tehdy, není-li výsledek už v cache.

---

## 7. Cache

### 7.1 `LruCache<TKey, TValue>`

Obecná LRU implementace (`Dictionary` + `LinkedList`), thread-safe přes `lock`.
Při vyřazení položky volá `onEvicted` callback → `IDisposable.Dispose()` na bitmapě.

### 7.2 `ImageCache`

Dvě instance `LruCache`:

| Cache | Kapacita | Obsah |
|-------|----------|-------|
| Plné obrázky | **10** | posledních 10 zobrazených / předběžně načtených snímků (2560 px) |
| Miniatury    | **64** | miniatury 240 px (paměťově zanedbatelné) |

Kapacita 10 splňuje požadavek „10 předchozích fotografií v paměti“. Prefetch okno ±2 se do stejné
cache vejde (5 aktivních + 5 historických).

**Odhad paměti:** 2560 × 1707 × 4 B ≈ 17,5 MB / snímek → cca **175 MB** pro plnou cache.
Konstanty jsou na jednom místě (`ImageCache.FullImageCapacity`, `MaxDisplayEdge`) a lze je snížit.

### 7.3 Invalidace

- Změna hlavní složky → `Clear()` obou cache.
- Přesun souboru → cache položka se **přemapuje** ze staré cesty na novou (`Rename(oldKey, newKey)`),
  bitmapa se nezahazuje, takže se po přesunu nic znovu nenačítá.
- Změna varianty (JPG ↔ CR2) → jiný `CacheKey`, načte se samostatně.

---

## 8. Práce se soubory

### 8.1 Skenování (`PhotoScanner`)

```csharp
ScanResult Scan(string rootPath, bool includeFilterFolders)
```

1. Vylistuje soubory v kořeni (nerekurzivně) s podporovanou příponou.
2. Pokud `includeFilterFolders`, přidá soubory z `edit`, `archive`, `delete` (nerekurzivně).
3. Seskupí podle `(složka, název bez přípony)` → `PhotoItem`.
4. Kategorie položky se odvodí z názvu složky, ve které leží.
5. Seřadí: nejdřív podle kategorie (`None` první), pak podle názvu přirozeným řazením
   (`IMG_2` před `IMG_10`) — vlastní `NaturalStringComparer`.

Skenování běží na `Task.Run` (může jít o tisíce souborů), UI zobrazuje indikátor načítání.

### 8.2 Přesun (`PhotoFileMover`)

```csharp
MoveRecord Move(PhotoItem item, PhotoCategory target, string rootPath)
```

- Cílová složka: `rootPath/<edit|archive|delete>`, vytvoří se při první potřebě.
- `target == PhotoCategory.None` → přesun zpět do kořene.
- Přesouvají se **všechny varianty** položky.
- Kolize názvů: `IMG_0042.JPG` → `IMG_0042 (1).JPG`; hledá se první volné `n`.
  Kolizní přípona se aplikuje **shodně na všechny varianty skupiny**, aby zůstaly spárované.
- Přesun v rámci stejného svazku = `File.Move` (atomický, rychlý). Napříč svazky `File.Move`
  interně kopíruje — akceptovatelné, podsložky jsou vždy na stejném svazku jako kořen.
- Pokud je položka už v cílové kategorii → no-op, jen se přejde na další fotografii.
- Vrací `MoveRecord` (seznam dvojic starých a nových cest + předchozí kategorie) pro undo.

### 8.3 Undo

Zásobník posledních **20** `MoveRecord`. `Ctrl+Z` / `Backspace` vrátí soubory na původní cesty,
obnoví kategorii položky, přemapuje cache a nastaví index na danou položku.

### 8.4 Bezpečnost

- `delete` je běžná podsložka — aplikace **nikdy nemaže soubory**.
- Všechny operace se souborovým systémem jsou v `try/catch`; chyba se zobrazí ve stavovém řádku
  a položka zůstane v původním stavu.
- Cesty se normalizují přes `Path.GetFullPath`; kontroluje se, že cíl leží pod kořenem.

---

## 9. Uživatelské rozhraní

### 9.1 Layout okna

```
┌────────────────────────────────────────────────────────────────────┐
│ [Zvolit složku]  C:\Photos   ☑ Zahrnout edit/archive/delete        │ ← horní lišta
├─────┬──────────────────────────────────────────────────────┬───────┤
│     │                                                      │       │
│  ◀  │                                                      │   ▶   │
│ ┌──┐│                  FOTOGRAFIE                          │┌──┐   │
│ │pr││                (Uniform, celá plocha)                ││ná│   │
│ │ev││                                                      ││sl│   │
│ └──┘│                                       ┌────────────┐ │└──┘   │
│     │                                       │ ● ARCHIVE  │ │       │  ← odznak kategorie
├─────┴──────────────────────────────────────────────────────┴───────┤
│  [E Edit]   [A Archive]   [D Delete]   [↩ Zpět do kořene]  [Undo]  │ ← spodní lišta
│  IMG_0042  ·  12 / 348  ·  JPG | CR2  ·  4256×2832  ·  8,4 MB      │ ← stavový řádek
└────────────────────────────────────────────────────────────────────┘
```

- Fotografie: `Image` se `Stretch="Uniform"`, `RenderOptions.BitmapInterpolationMode="HighQuality"`.
- Miniatury: 160 px široké panely vlevo/vpravo, klik = skok na danou fotografii.
- Odznak kategorie: barevný `Border` v pravém dolním rohu snímku
  (Edit = modrá `#2D7DD2`, Archive = zelená `#4C9F70`, Delete = červená `#D64550`).
  Tlačítko aktivní kategorie je zvýrazněné.
- Motiv: Fluent Dark (tmavé pozadí nezkresluje vnímání fotografie).

### 9.2 Klávesové zkratky

| Klávesa | Akce |
|---------|------|
| `←` / `PageUp` | předchozí fotografie |
| `→` / `PageDown` | následující fotografie |
| `Home` / `End` | první / poslední |
| `E`, `Space` | zařadit do `edit` |
| `A`, `K` | zařadit do `archive` |
| `D`, `Delete` | zařadit do `delete` |
| `R` | vrátit do kořenové složky |
| `Tab` | přepnout variantu (JPG ↔ CR2) |
| `Ctrl+Z`, `Backspace` | undo posledního přesunu |
| `O`, `Ctrl+O` | otevřít složku |
| `F11` | fullscreen |

Všechny zkratky se odchytávají v `MainWindow.OnKeyDown` s `Handled = true`; tlačítka mají
`Focusable="False"`, takže `Space` ani šipky nikdy neaktivují zafokusované tlačítko.

**Navigační příkazy jsou synchronní**, kategorizace asynchronní. Kdyby byla navigace asynchronní,
`AsyncRelayCommand` by při podrženém šipkovém opakování zahazoval stisky, dokud běží předchozí
dekódování. Naopak u kategorizace je `AllowConcurrentExecutions = false` žádoucí — brání tomu,
aby dvojí stisk přesunul tutéž fotografii dvakrát.

### 9.3 Chování po zařazení

1. Soubory se přesunou.
2. Položka dostane příznak kategorie (odznak se krátce zobrazí).
3. Automaticky se přejde na následující fotografii.
4. Položka **zůstává v seznamu** (mění se jen její kategorie a cesty) — díky tomu funguje undo
   a je vidět, co už bylo zařazeno.

### 9.4 Stavy

| Stav | Zobrazení |
|------|-----------|
| Nezvolena složka | uvítací panel s velkým tlačítkem „Zvolit složku“ |
| Skenování | progress ring + „Načítám…“ |
| Prázdná složka | „Ve složce nejsou žádné podporované fotografie“ |
| Dekódování běží | progress ring přes plochu, poslední snímek zůstává zobrazen |
| Chyba dekódování | ikona + text chyby |

---

## 10. Použité knihovny

| Balíček | Verze | Účel |
|---------|-------|------|
| `Avalonia` + `Avalonia.Desktop` + `Avalonia.Themes.Fluent` | 11.3.20 | UI framework |
| `Avalonia.Fonts.Inter` | 11.3.20 | konzistentní písmo napříč OS |
| `Avalonia.Diagnostics` (jen Debug) | 11.3.20 | inspektor vizuálního stromu |
| `CommunityToolkit.Mvvm` | 8.4.2 | `ObservableObject`, `[RelayCommand]` |
| `SkiaSharp` | 2.88.9 | dekódování a škálování obrázků |
| `MetadataExtractor` | 2.9.3 | EXIF orientace u RAW |
| `xunit` + `Avalonia.Headless.XUnit` | 2.9.x / 11.3.20 | testy včetně vykreslení okna |

Všechny balíčky výrazně překračují hranici 100 000 stažení.

`SkiaSharp` je záměrně přišpendlen na **2.88.9**, tedy na verzi, kterou si táhne Avalonia 11.3.20.
Novější 3.x by vedla ke dvěma nekompatibilním nativním knihovnám v jednom procesu.

Extrakce JPEG náhledu z RAW je **vlastní kód** (`TiffPreviewExtractor`) — viz upozornění v §12.

---

## 11. Testy

Testy pracují nad dočasnou složkou (`Path.GetTempPath()`) a nad skutečně zakódovanými JPEG daty,
ne nad mocky souborového systému.

**Bez UI (xUnit):**

- `PhotoScannerTests` — seskupení JPG+CR2, filtrační složky zapnuté/vypnuté, přirozené řazení,
  ignorování nepodporovaných přípon, odvození kategorie ze složky.
- `PhotoFileMoverTests` — přesun skupiny, vytvoření cílové složky, řešení kolize názvů se
  zachováním párování variant, přesun zpět do kořene, undo.
- `PhotoLibraryTests` — navigace na hranicích seznamu, sousedé, událost `PhotoRelocated`, undo.
- `LruCacheTests` — kapacita 10, pořadí vyřazování, `onEvicted`, `Rename`.
- `TiffPreviewExtractorTests` — nalezení JPEG stripu v syntetickém CR2-like kontejneru, výběr
  největšího náhledu, chování bez náhledu.

**S headless Avalonia (`Avalonia.Headless.XUnit`):**

- `ImagePipelineTests` — skutečné dekódování JPEG i RAW náhledu, downscaling, cache hit,
  `Remap` po přesunu, chybový stav, a **ověření, že loader nikdy nedekóduje dva snímky naráz**.
- `MainWindowViewModelTests` — načtení složky, kategorizace + posun na další, undo, přepnutí
  varianty, naplnění prefetch okna ±2.
- `MainWindowRenderTests` — vykreslení skutečného okna (kontrola XAML a compiled bindings) a
  ovládání klávesnicí (`D`, `←`, `→`).

---

## 12. Známá omezení a upozornění

1. **Windows 7 není podporován a nelze to obejít ani self-contained publikací.**
   Microsoft uvádí Windows 7 SP1 pro .NET 8/9/10 explicitně jako ❌; minimum je Windows 10 1607.
   Poslední .NET s podporou Win7 byla .NET 6 (konec podpory 12. 11. 2024).
   Self-contained build nepomůže, protože nekompatibilní je **samotný runtime**, ne jeho
   instalace — binárky .NET 7+ volají Win32 API, která ve Windows 7 neexistují. Zabalit runtime
   do zipu tedy problém nijak neřeší.
   Jediná reálná cesta k Win7 by bylo multi-targeting na `net6.0` a samostatný „legacy“ build —
   viz §13.
   **Specifikace předpokládá Windows 10 (1607+) / 11 a Linux.**
2. **RAW náhledy místo plné demozaikace.** Neexistuje udržovaná .NET knihovna pro dekódování RAW
   s > 100 000 staženími (obálky nad LibRaw mají řádově tisíce stažení, Magick.NET RAW delegáta
   nemá zkompilovaného). Proto se používá JPEG náhled vložený v RAW souboru — u Canon CR2 je
   v plném rozlišení, takže pro třídění fotografií plně dostačuje. Vlastní `TiffPreviewExtractor`
   je tedy nutný kompromis oproti externí knihovně.
3. **Canon CR3** není podporován (jiný kontejner než TIFF).
4. **Nerekurzivní sken** — podsložky jiné než `edit` / `archive` / `delete` se ignorují.
5. **Paměť** — plná cache zabere cca 175 MB. Pro slabší stroje lze snížit `MaxDisplayEdge`.

---

## 13. Distribuce (GitHub Actions)

Workflow [`.github/workflows/release.yml`](../.github/workflows/release.yml) se spouští ručně
(`workflow_dispatch`) a bere vstup `version` (například `1.2`) a volitelný přepínač `prerelease`.

### 13.1 Průběh

| Job | Běží na | Co dělá |
|-----|---------|---------|
| `validate` | ubuntu | ověří formát verze (`1.2`, `1.2.3`, …) a že tag `v<verze>` ještě neexistuje |
| `test` | ubuntu **i** windows | `restore` → `build -c Release` → `dotnet test` na obou OS |
| `publish` | windows / ubuntu | self-contained single-file publish pro `win-x64`, `win-x86`, `linux-x64` |
| `release` | ubuntu | vytvoří GitHub Release s tagem `v<verze>` a připojí všechny zipy |

Každý job závisí na předchozím, takže **release nikdy nevznikne z kódu, který neprošel testy**.

### 13.2 Parametry publikace

```
--self-contained true
-p:PublishSingleFile=true
-p:IncludeNativeLibrariesForSelfExtract=true
-p:EnableCompressionInSingleFile=true
-p:DebugType=none
-p:Version=<verze>
```

Runtime .NET i nativní knihovny (SkiaSharp, HarfBuzz, ANGLE) jsou uvnitř jednoho spustitelného
souboru. Uživatel **nepotřebuje nainstalovaný .NET** — stáhne zip, rozbalí, spustí.
Výsledek: cca 46 MB pro `win-x64`, 43 MB pro `win-x86`.

Nativní knihovny se při prvním spuštění rozbalí do dočasné složky; proto
`IncludeNativeLibrariesForSelfExtract`. Bez něj by vedle `.exe` musely ležet volně.

### 13.3 Balení

- Windows: `Compress-Archive`.
- Linux: `zip` (Info-ZIP), protože jako jediný zachová příznak spustitelnosti souboru `PhotoSort`.

Do každého archivu se přidává `README.md` a technická specifikace.

### 13.4 Proč není v matici build pro Windows 7

Viz §12.1 — nešlo by jen o jiný RID. Bylo by potřeba:

1. multi-targeting projektu na `net6.0` (mimo podporu, bez bezpečnostních oprav),
2. přepsat kód, který používá novější API: `System.Threading.Lock` (.NET 9+),
   `Stream.ReadExactly` a `Stream.ReadAtLeast` (.NET 7+),
3. do CI přidat .NET 6 SDK,
4. smířit se s tím, že Avalonia oficiálně deklaruje Windows 8+ a na Win7 by šlo o neotestovaný
   provoz se softwarovým rendrováním,
5. na cílovém stroji stejně doinstalovat KB2999226, KB3063858 a VC++ 2015–2019 Redistributable.

Proto to **není** součástí výchozí matice.
