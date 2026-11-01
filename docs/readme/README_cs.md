# VoxQueue — Čeština

Dávková syntéza řeči pro jakýkoli server kompatibilní s OpenAI `/v1/audio` (Piper, VoiceStudio, Aether Voice Hub).

---

## Rychlý start

**Windows** — stáhněte `publish/windows/VoxQueue.exe` a spusťte. Nic se neinstaluje.

**Linux** — `publish/linux/VoxQueue`, nebo ze zdrojů:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Nastavení → Připojení** — adresa serveru, výchozí `http://127.0.0.1:3900`
2. **Obnovit** — načte seznam hlasů
3. Zvolte režim, vložte text (nebo přetáhněte `.txt` do pole)
4. **Zpracovat text** — ukáže, kolik promluv parser našel
5. **Start**

Ukázkové soubory pro každý režim jsou v `samples/`.

---

## Tři režimy

Režim rozhoduje o **jedné věci**: jak se text dělí na promluvy a jak se soubory pojmenují.

| Režim | Vstup | Výstup |
|---|---|---|
| **Trénink** | jeden řádek = jedna promluva | `wav/0000.wav` + `metadata.csv` |
| **Dabing** | `POSTAVA: dialog` | `voices/POSTAVA/POSTAVA_0000_2.41s.wav` |
| **Dávka** | bloky oddělené `###` | `wav/0000.wav` + `0000.txt` |

---

## Značky výslovnosti

Model TTS čte písmena, ne pravopis. `Aether` vyjde jako „A-e-ther“. `2024` se hláskuje. Čísla se rozvádějí v základním tvaru, takže jakýkoli skloňovaný tvar je chybný. Dejte motoru fonetický zápis a v přepisu zachovejte správný pravopis:

```
{Eter|Aether} je online.
Rok {dva tisíce dvacátý čtvrtý|2024}.
{Pi Em|PM} poslal zprávu.
```

- **první** forma jde do TTS motoru — ta se vyslovuje
- **druhá** forma jde do `metadata.csv` a do přepisu `.txt`

### Pravidla

| Zápis | Účinek |
|---|---|
| `{vyslovované\|psané}` | vysloví první, zapíše druhé |
| `{text}` bez svislé čáry | zůstane doslova — není značka |
| prázdná strana (`{\|text}`, `{text\|}`) | zůstane doslova — nic se nesmaže |
| `{` bez `}` | zůstane doslova |
| chybná značka | nikdy nezničí text |

Psaná forma je **nedotknutelná**: stejné skloňování, stejný pravopis, stejná interpunkce.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## Hotový prompt pro model AI

Vložte do libovolného asistenta a získáte skript, který VoxQueue přijme přímo.

```
Připrav soubor `.txt` pro import do VoxQueue. Jeden řádek = jedna promluva. Piš
přirozenou češtinou. Kde by syntéza řeči přečetla něco špatně — cizí vlastní
jména, víceciferná čísla, zkratky, symboly, jednotky — použij značku
`{výslovnost|pravopis}`, například `{Eter|Aether}` nebo
`{dva tisíce dvacátého čtvrtého|2024}`. Forma vpravo musí být přesně to, co má
být v přepisu, s původním skloňováním. Neoznačuj běžná slova, jednociferná čísla
ani data, která znějí přirozeně. Nepřekládej. Neopravuj gramatiku ve zdrojovém
textu. Na konci vypiš všechny řádky, u kterých sis výslovností nebyl jistý.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Další jazyky: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
