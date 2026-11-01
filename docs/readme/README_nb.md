# VoxQueue — Norsk bokmål

Batch-tekst-til-tale for enhver OpenAI-kompatibel `/v1/audio`-server (Piper, VoiceStudio, Aether Voice Hub).

---

## Hurtigstart

**Windows** — last ned `publish/windows/VoxQueue.exe` og kjør. Ingenting å installere.

**Linux** — `publish/linux/VoxQueue`, eller fra kilden:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Innstillinger → Tilkobling** — serveradresse, standard `http://127.0.0.1:3900`
2. **Oppdater** — laster inn stemmelisten
3. Velg en modus, lim inn tekst (eller dra en `.txt` til feltet)
4. **Bearbeid tekst** — viser hvor mange ytringer parseren fant
5. **Start**

Eksempelfiler for hver modus ligger i `samples/`.

---

## Tre moduser

Modusen avgjør **én ting**: hvordan tekst deles i ytringer og hvordan filer navngis.

| Modus | Inndata | Utdata |
|---|---|---|
| **Trening** | én linje = én ytring | `wav/0000.wav` + `metadata.csv` |
| **Dubbing** | `FIGUR: dialog` | `voices/FIGUR/FIGUR_0000_2.41s.wav` |
| **Batch** | blokker skilt med `###` | `wav/0000.wav` + `0000.txt` |

---

## Uttalemarkører

En TTS-modell leser bokstaver, ikke stavemåte. `Aether` blir «A-e-ther». `2024` staves. Tall skrives ut i grunnform, så enhver bøyd form er feil. Gi motoren en fonetisk stavemåte og behold den korrekte i transkripsjonen:

```
{Eter|Aether} er på nett.
År {to-tusen-fire-og-tjue|2024}.
{Pi Em|PM} sendte rapporten.
```

- **første** form går til TTS-motoren — det er den som sies
- **andre** form går til `metadata.csv` og `.txt`-transkripsjonen

### Regler

| Notasjon | Virkning |
|---|---|
| `{talt\|skrevet}` | sier det første, skriver det andre |
| `{tekst}` uten strek | forblir bokstavelig — ikke en markør |
| tom side (`{\|tekst}`, `{tekst\|}`) | forblir bokstavelig — ingenting slettes |
| `{` uten `}` | forblir bokstavelig |
| feilaktig markør | ødelegger aldri tekst |

Den skrevne formen er **ukrenkelig**: samme bøyning, samme stavemåte, samme tegnsetting.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## Klar-til-kopiering-prompt for en AI-modell

Lim inn i en hvilken som helst assistent for å få et manus VoxQueue godtar direkte.

```
Klargjør en `.txt`-fil for import i VoxQueue. Én linje = én ytring. Skriv på
naturlig norsk. Der en talesyntese ville lese noe feil — utenlandske egennavn,
flersifrede tall, forkortelser, symboler, enheter — bruk markøren
`{uttale|skrivemåte}`, for eksempel `{Eter|Aether}` eller
`{to-tusen-fire-og-tjue|2024}`. Formen til høyre må være nøyaktig det som skal
stå i transkripsjonen, med opprinnelig bøyning. Ikke marker vanlige ord,
ensifrede tall eller datoer som lyder naturlig. Ikke oversett. Ikke rett
grammatikk i kildeteksten. List til slutt opp alle linjer der du var usikker på
uttalen.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Andre språk: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
