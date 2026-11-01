# VoxQueue — Suomi

Eräpuheenteko mille tahansa OpenAI-yhteensopivalle `/v1/audio`-palvelimelle (Piper, VoiceStudio, Aether Voice Hub).

---

## Pika-aloitus

**Windows** — lataa `publish/windows/VoxQueue.exe` ja käynnistä. Mitään ei tarvitse asentaa.

**Linux** — `publish/linux/VoxQueue`, tai lähdekoodista:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Asetukset → Yhteys** — palvelimen osoite, oletus `http://127.0.0.1:3900`
2. **Päivitä** — lataa ääniluettelon
3. Valitse tila, liitä teksti (tai vedä `.txt` kenttään)
4. **Jäsennä teksti** — näyttää, kuinka monta repliikkiä jäsennin löysi
5. **Aloita**

Esimerkkitiedostot joka tilaan ovat kansiossa `samples/`.

---

## Kolme tilaa

Tila päättää **yhden asian**: miten teksti jaetaan repliikkeihin ja miten tiedostot nimetään.

| Tila | Syöte | Tuloste |
|---|---|---|
| **Koulutus** | yksi rivi = yksi repliikki | `wav/0000.wav` + `metadata.csv` |
| **Jälkiäänitys** | `HAHMO: dialogi` | `voices/HAHMO/HAHMO_0000_2.41s.wav` |
| **Erä** | lohkot erotettu `###` | `wav/0000.wav` + `0000.txt` |

---

## Ääntämismerkinnät

TTS-malli lukee kirjaimia, ei kirjoitusasua. `Aether` tulee muodossa "A-e-ther". `2024` tavutetaan. Numerot kirjoitetaan auki perusmuodossa, joten mikä tahansa taivutusmuoto on väärä. Anna moottorille foneettinen kirjoitusasu ja säilytä oikea litteroinnissa:

```
{Eter|Aether} on verkossa.
Vuosi {kaksituhattakaksikymmentäneljä|2024}.
{Pi Em|PM} lähetti raportin.
```

- **ensimmäinen** muoto menee TTS-moottorille — se lausutaan
- **toinen** muoto menee tiedostoon `metadata.csv` ja `.txt`-litterointiin

### Säännöt

| Merkintä | Vaikutus |
|---|---|
| `{lausuttu\|kirjoitettu}` | lausuu ensimmäisen, kirjoittaa toisen |
| `{teksti}` ilman pystyviivaa | pysyy kirjaimellisena — ei merkintä |
| tyhjä puoli (`{\|teksti}`, `{teksti\|}`) | pysyy kirjaimellisena — mitään ei poisteta |
| `{` ilman `}` | pysyy kirjaimellisena |
| virheellinen merkintä | ei koskaan tuhoa tekstiä |

Kirjoitettu muoto on **loukkaamaton**: sama taivutus, sama kirjoitusasu, sama välimerkitys.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## Valmis kehote AI-mallille

Liitä mihin tahansa avustajaan saadaksesi käsikirjoituksen, jonka VoxQueue hyväksyy sellaisenaan.

```
Valmistele `.txt`-tiedosto VoxQueue-tuontia varten. Yksi rivi = yksi repliikki.
Kirjoita luonnollisella suomella. Kaikkialla, missä puhesynteesi lukisi jotain
väärin — vieraat erisnimet, moninumeroiset luvut, lyhenteet, symbolit,
yksiköt — käytä merkintää `{ääntäminen|kirjoitusasu}`, esimerkiksi
`{Eter|Aether}` tai `{kaksituhattakaksikymmentäneljä|2024}`. Oikeanpuoleisen
muodon on oltava täsmälleen se, mikä litterointiin tulee, alkuperäisessä
taivutusmuodossa. Älä merkitse tavallisia sanoja, yksinumeroisia lukuja tai
luontevasti lausuttavia päivämääriä. Älä käännä. Älä korjaa lähdetekstin
kielioppia. Listaa lopuksi kaikki rivit, joiden ääntämisestä olit epävarma.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Muut kielet: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
