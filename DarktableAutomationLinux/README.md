# Dávkový AI denoise RAW souborů přes darktable (Linux)

Pro každou fotografii ve složce vytvoří **DNG s AI odšuměním** na zadané úrovni.
Používá nativní modul *neural restore* z darktable 5.6 (úloha `raw denoise`,
model RawNIND), který odšumuje přímo CFA mozaiku před demozajkováním.

Originální RAW soubory se nikdy nemění — vedle nich vznikne
`<název>_raw-denoise.dng`.

## Předpoklady

1. **darktable 5.6 nebo novější** s AI podsystémem (oficiální buildy ho mají).
2. **Jednorázové nastavení v GUI** — automatizace ho neumí udělat za vás:
   spusťte darktable, otevřete *preferences → AI*, zapněte hlavní vypínač
   (`enable AI features`), stáhněte model pro úlohu `rawdenoise`
   (např. `rawdenoise-nind`) a zaškrtněte u něj `enabled`.
3. `xvfb-run` (balíček `xvfb`) — volitelné, ale doporučené: darktable pak běží
   neviditelně na pozadí. Bez něj se použije vaše grafická relace (`--gui`).
4. Během běhu **nesmí být darktable spuštěný** — zamyká si sdílenou databázi.

## Použití

```bash
chmod +x denoise-folder.sh          # jen poprvé

./denoise-folder.sh ~/Pictures/2026-09-15
./denoise-folder.sh -s 70 ~/Pictures/2026-09-15
./denoise-folder.sh -s 80 -o ~/Pictures/denoised -r ~/Pictures/2026-09-15
```

| Přepínač | Význam |
|---|---|
| `-s, --strength <0-100>` | síla odšumění, 100 = plný výstup modelu (výchozí) |
| `-o, --output <složka>` | kam ukládat DNG (výchozí: vedle zdrojového souboru) |
| `-m, --model <id>` | konkrétní rawdenoise model (výchozí: aktivní v darktable) |
| `-r, --recursive` | zpracovat i podsložky |
| `-t, --timeout <s>` | ukončit, když se nový DNG neobjeví takto dlouho (výchozí 900) |
| `-b, --darktable <cesta>` | cesta k binárce darktable (nebo proměnná `DARKTABLE_BIN`) |
| `-a, --action <cesta>` | ruční cesta k akci tlačítka *process* (viz Potíže) |
| `--gui` | zobrazit okno darktable místo skrytí v Xvfb |
| `--keep-workdir` | nechat dočasnou složku (logy) pro ladění |

Skript průběžně vypisuje, které DNG už vznikly, a skončí návratovým kódem 0
jen tehdy, když se podařilo vytvořit všechny.

### Síla odšumění

`--strength` je přesně ten posuvník *strength*, který má modul v GUI: lineární
prolnutí mezi původním RAW (0 %) a plným výstupem modelu (100 %) na úrovni
senzorových dat. Nižší hodnoty vrací část původního šumu, a tím i zrnitost.

## Jak to funguje

darktable nabízí AI raw denoise **jen jako GUI modul** — `darktable-cli` pro něj
nemá přepínač a Lua API `darktable.ai` sice umí inference nad tenzory, ale nemá
operaci pro prolnutí dvou tenzorů, takže by v něm sílu odšumění nešlo
respektovat. Skript proto spustí plnohodnotný darktable a obslouží modul za vás:

1. vytvoří dočasnou pracovní složku se seznamem souborů,
2. zazálohuje `darktablerc` a předá nastavení přes `--conf`
   (síla, výstupní složka, záložka *raw denoise*, vypnutý zápis XMP),
3. spustí darktable s **dočasnou knihovnou** (`--library`), takže se vaše
   sbírka fotografií nezaplevelí, a s `--luacmd`, které načte `denoise_batch.lua`,
4. Lua skript soubory naimportuje, označí je a stiskne tlačítko *process*
   v modulu *neural restore*, pak hlídá vznikající DNG a nakonec darktable ukončí,
5. launcher obnoví původní `darktablerc` a uklidí dočasnou složku.

Po skončení tedy v systému nezůstane nic kromě nových DNG souborů.

## Potíže

**„no rawdenoise model is active“** — nemáte v *preferences → AI* zapnuté AI
nebo aktivovaný model pro úlohu `rawdenoise`. Viz Předpoklady.

**Nevznikne žádný DNG a skript hlásí timeout** — nejpravděpodobněji se nepodařilo
trefit cestu k akci tlačítka *process*. Zjistíte ji přesně: v darktable otevřete
*preferences → shortcuts*, najděte akci `process` u modulu *neural restore*,
vyberte ji a stiskněte `Ctrl+C` — do schránky se zkopíruje hotové volání
`darktable.gui.action("...", ...)`. Cestu z něj předejte přes `-a`:

```bash
./denoise-folder.sh -a "lib/neural restore/process" ~/Pictures/2026-09-15
```

**Trvá to velmi dlouho** — bez GPU akcelerace je raw denoise řádově minuty na
snímek. Zkontrolujte v *preferences → AI*, že je vybraný hardwarový akcelerátor,
a případně zvyšte `--timeout`.

**„darktable is running“** — zavřete běžící darktable, jinak nelze otevřít
sdílenou databázi `data.db`.

**Flatpak instalace** — předejte cestu ke spouštěči přes `-b`; počítejte s tím,
že flatpak má vlastní konfigurační složku i omezený přístup k souborům.

## Omezení

- Monochromatické senzory modul nepodporuje (pro ně je určená záložka *denoise*).
- Výstup je u Bayerových senzorů Bayer CFA DNG, u X-Trans LinearRaw DNG.
- Soubory, které už mají v názvu `_raw-denoise`, se přeskakují, aby druhé
  spuštění neodšumovalo vlastní výstup.
