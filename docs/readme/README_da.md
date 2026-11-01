# VoxQueue — Dansk

Batch-tekst-til-tale til enhver OpenAI-kompatibel `/v1/audio`-server (Piper, VoiceStudio, Aether Voice Hub).

---

## Hurtig start

**Windows** — hent `publish/windows/VoxQueue.exe` og kør. Intet at installere.

**Linux** — `publish/linux/VoxQueue`, eller fra kilden:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Indstillinger → Forbindelse** — serveradresse, standard `http://127.0.0.1:3900`
2. **Opdater** — indlæser stemmelisten
3. Vælg en tilstand, indsæt tekst (eller træk en `.txt` til feltet)
4. **Behandl tekst** — viser hvor mange ytringer parseren fandt
5. **Start**

Eksempelfiler til hver tilstand ligger i `samples/`.

---

## Tre tilstande

Tilstanden afgør **én ting**: hvordan tekst deles i ytringer, og hvordan filer navngives.

| Tilstand | Inddata | Uddatа |
|---|---|---|
| **Træning** | én linje = én ytring | `wav/0000.wav` + `metadata.csv` |
| **Dubbing** | `FIGUR: dialog` | `voices/FIGUR/FIGUR_0000_2.41s.wav` |
| **Batch** | blokke adskilt af `###` | `wav/0000.wav` + `0000.txt` |

---

## Udtalemarkeringer

En TTS-model læser bogstaver, ikke stavning. `Aether` bliver "A-e-ther". `2024` staves. Tal skrives ud i grundform, så enhver bøjet form er forkert. Giv motoren en fonetisk stavning og behold den korrekte i transskriptionen:

```
{Eter|Aether} er online.
År {to-tusind-fire-og-tyve|2024}.
{Pi Em|PM} sendte rapporten.
```

- **første** form går til TTS-motoren — det er den, der siges
- **anden** form går til `metadata.csv` og `.txt`-transskriptionen

### Regler

| Notation | Virkning |
|---|---|
| `{talt\|skrevet}` | siger det første, skriver det andet |
| `{tekst}` uden streg | forbliver bogstaveligt — ikke en markering |
| tom side (`{\|tekst}`, `{tekst\|}`) | forbliver bogstaveligt — intet slettes |
| `{` uden `}` | forbliver bogstaveligt |
| fejlbehæftet markering | ødelægger aldrig tekst |

Den skrevne form er **ukrænkelig**: samme bøjning, samme stavning, samme tegnsætning.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## Klar-til-kopiering-prompt til en AI-model

Indsæt i en vilkårlig assistent for at få et manuskript, VoxQueue accepterer direkte.

```
Forbered en `.txt`-fil til import i VoxQueue. Én linje = én ytring. Skriv på
naturligt dansk. Hvor en talesyntese ville læse noget forkert — udenlandske
egennavne, flercifrede tal, forkortelser, symboler, enheder — brug markeringen
`{udtale|stavemåde}`, for eksempel `{Eter|Aether}` eller
`{to-tusind-fire-og-tyve|2024}`. Formen til højre skal være præcis det, der skal
stå i transskriptionen, med den oprindelige bøjning. Markér ikke almindelige ord,
etcifrede tal eller datoer, der lyder naturligt. Oversæt ikke. Ret ikke grammatik
i kildeteksten. List til sidst alle linjer, hvor du var usikker på udtalen.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Andre sprog: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
