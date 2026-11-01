# VoxQueue — Polski

Wsadowa synteza mowy dla dowolnego serwera zgodnego z OpenAI `/v1/audio` (Piper, VoiceStudio, Aether Voice Hub).

---

## Szybki start

**Windows** — pobierz `publish/windows/VoxQueue.exe` i uruchom. Nic nie trzeba instalować.

**Linux** — `publish/linux/VoxQueue`, albo ze źródeł:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Ustawienia → Połączenie** — adres serwera, domyślnie `http://127.0.0.1:3900`
2. **Odśwież** — wczytuje listę głosów
3. Wybierz tryb, wklej tekst (albo przeciągnij plik `.txt` na pole)
4. **Przetwórz tekst** — pokaże, ile kwestii znalazł parser
5. **Start**

Próbki dla każdego trybu leżą w `samples/`.

---

## Trzy tryby

Tryb decyduje o **jednej rzeczy**: jak tekst jest dzielony na kwestie i jak nazywane są pliki.

| Tryb | Wejście | Wyjście |
|---|---|---|
| **Trening** | jedna linia = jedna kwestia | `wav/0000.wav` + `metadata.csv` |
| **Dubbing** | `POSTAĆ: dialog` | `voices/POSTAĆ/POSTAĆ_0000_2.41s.wav` |
| **Wsadowy** | bloki rozdzielone `###` | `wav/0000.wav` + `0000.txt` |

---

## Znaczniki wymowy

Model czyta litery, nie pisownię. `Aether` wychodzi jako „A-e-ther”. `2024` zostaje przeliterowane. Liczby są rozwijane tylko do mianownika. Podaj silnikowi zapis fonetyczny, zachowując poprawną pisownię w transkrypcie:

```
{Eter|Aether} jest online.
{dwa tysiące dwudziestego czwartego|2024} rok.
{pi em|PM} wysłał raport.
```

- **pierwsza** forma idzie do silnika TTS — to ona jest wymawiana
- **druga** forma trafia do `metadata.csv` i do transkryptu `.txt`

### Zasady

| Zapis | Efekt |
|---|---|
| `{mówione\|pisane}` | mówi pierwsze, zapisuje drugie |
| `{tekst}` bez kreski | zostaje dosłownie — nie jest znacznikiem |
| pusta strona (`{\|tekst}`, `{tekst\|}`) | zostaje dosłownie — nic nie jest kasowane |
| `{` bez `}` | zostaje dosłownie |
| niepoprawny znacznik | nigdy nie niszczy tekstu |

Forma pisana jest **nienaruszalna**: ta sama odmiana, ta sama pisownia, ta sama interpunkcja.

Lista słów jest zależna od języka — zbuduj ją, słuchając głosu referencyjnego i zapisując, co wymawia źle.

---

## Gotowy prompt dla modelu AI

Wklej to do dowolnego asystenta, żeby dostać skrypt, który VoxQueue przyjmie bez zmian.

```
Przygotuj plik `.txt` do importu w VoxQueue. Jedna linia = jedna kwestia. Pisz
naturalną polszczyzną. Tam, gdzie syntezator mowy przeczytałby coś niepoprawnie —
nazwy własne obce, liczby wielocyfrowe, skrótowce, symbole, jednostki — użyj
znacznika `{wymowa|pisownia}`, na przykład `{Eter|Aether}` albo
`{dwa tysiące dwudziestego czwartego|2024}`. Forma po prawej musi być dokładnie
tą, która ma się znaleźć w transkrypcie, z zachowaniem oryginalnej odmiany. Nie
oznaczaj zwykłych słów, liczb jednocyfrowych ani dat, które brzmią naturalnie.
Nie tłumacz. Nie poprawiaj gramatyki w tekście źródłowym. Na końcu wypisz
wszystkie linie, w których nie byłeś pewien wymowy.
```

---

Od zera potrzebne jest minimum 5–8 godzin audio; celuj w 8–10.

Inne języki: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
