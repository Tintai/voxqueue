# VoxQueue — Slovenčina

Dávková syntéza reči pre akýkoľvek server kompatibilný s OpenAI `/v1/audio` (Piper, VoiceStudio, Aether Voice Hub).

---

## Rýchly štart

**Windows** — stiahnite `publish/windows/VoxQueue.exe` a spustite. Nič sa neinštaluje.

**Linux** — `publish/linux/VoxQueue`, alebo zo zdrojov:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Nastavenia → Pripojenie** — adresa servera, predvolená `http://127.0.0.1:3900`
2. **Obnoviť** — načíta zoznam hlasov
3. Zvoľte režim, vložte text (alebo pretiahnite `.txt` do poľa)
4. **Spracovať text** — ukáže, koľko prehovorov parser našiel
5. **Štart**

Ukážkové súbory pre každý režim sú v `samples/`.

---

## Tri režimy

Režim rozhoduje o **jednej veci**: ako sa text delí na prehovory a ako sa súbory pomenujú.

| Režim | Vstup | Výstup |
|---|---|---|
| **Tréning** | jeden riadok = jeden prehovor | `wav/0000.wav` + `metadata.csv` |
| **Dabing** | `POSTAVA: dialóg` | `voices/POSTAVA/POSTAVA_0000_2.41s.wav` |
| **Dávka** | bloky oddelené `###` | `wav/0000.wav` + `0000.txt` |

---

## Značky výslovnosti

Model TTS číta písmená, nie pravopis. `Aether` vyjde ako „A-e-ther“. `2024` sa hláskuje. Čísla sa rozvádzajú v základnom tvare, takže akýkoľvek skloňovaný tvar je chybný. Dajte motoru fonetický zápis a v prepise zachovajte správny pravopis:

```
{Eter|Aether} je online.
Rok {dvetisíc dvadsiaty štvrtý|2024}.
{Pi Em|PM} poslal správu.
```

- **prvá** forma ide do TTS motora — tá sa vyslovuje
- **druhá** forma ide do `metadata.csv` a do prepisu `.txt`

### Pravidlá

| Zápis | Účinok |
|---|---|
| `{vyslovované\|písané}` | vysloví prvé, zapíše druhé |
| `{text}` bez zvislej čiary | zostane doslova — nie je značka |
| prázdna strana (`{\|text}`, `{text\|}`) | zostane doslova — nič sa nezmaže |
| `{` bez `}` | zostane doslova |
| chybná značka | nikdy nezničí text |

Písaná forma je **nedotknuteľná**: rovnaké skloňovanie, rovnaký pravopis, rovnaká interpunkcia.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## Hotový prompt pre model AI

Vložte do ľubovoľného asistenta a získate skript, ktorý VoxQueue prijme priamo.

```
Priprav súbor `.txt` pre import do VoxQueue. Jeden riadok = jeden prehovor. Píš
prirodzenou slovenčinou. Kde by syntéza reči prečítala niečo nesprávne — cudzie
vlastné mená, viacciferné čísla, skratky, symboly, jednotky — použi značku
`{výslovnosť|pravopis}`, napríklad `{Eter|Aether}` alebo
`{dvetisíc dvadsiateho štvrtého|2024}`. Forma vpravo musí byť presne to, čo má
byť v prepise, s pôvodným skloňovaním. Neoznačuj bežné slová, jednociferné čísla
ani dátumy, ktoré znejú prirodzene. Neprekladaj. Neopravuj gramatiku v zdrojovom
texte. Na konci vypíš všetky riadky, pri ktorých si si výslovnosťou nebol istý.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Ďalšie jazyky: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
