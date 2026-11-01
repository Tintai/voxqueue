# VoxQueue — Türkçe

Herhangi bir OpenAI uyumlu `/v1/audio` sunucusu için toplu metin-konuşma (Piper, VoiceStudio, Aether Voice Hub).

---

## Hızlı başlangıç

**Windows** — `publish/windows/VoxQueue.exe` indirip çalıştırın. Kurulum gerekmez.

**Linux** — `publish/linux/VoxQueue` veya kaynaktan:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Ayarlar → Bağlantı** — sunucu adresi, varsayılan `http://127.0.0.1:3900`
2. **Yenile** — ses listesini yükler
3. Bir mod seçin, metni yapıştırın (veya alana `.txt` sürükleyin)
4. **Metni işle** — ayrıştırıcının kaç replik bulduğunu gösterir
5. **Başlat**

Her mod için örnek dosyalar `samples/` içinde.

---

## Üç mod

Mod **tek bir şeye** karar verir: metnin repliklere nasıl bölüneceği ve dosyaların nasıl adlandırılacağı.

| Mod | Girdi | Çıktı |
|---|---|---|
| **Eğitim** | bir satır = bir replik | `wav/0000.wav` + `metadata.csv` |
| **Dublaj** | `KARAKTER: diyalog` | `voices/KARAKTER/KARAKTER_0000_2.41s.wav` |
| **Toplu** | `###` ile ayrılmış bloklar | `wav/0000.wav` + `0000.txt` |

---

## Telaffuz işaretleri

TTS modeli harfleri okur, yazımı değil. `Aether` "A-e-ther" olur. `2024` harf harf okunur. Sayılar temel biçimde açılır, bu yüzden çekimli her biçim yanlıştır. Motora fonetik yazım verin, transkriptte doğru yazımı koruyun:

```
{Eter|Aether} çevrimiçi.
Yıl {iki bin yirmi dört|2024}.
{Pi Em|PM} raporu gönderdi.
```

- **birinci** biçim TTS motoruna gider — söylenen odur
- **ikinci** biçim `metadata.csv` ve `.txt` transkriptine gider

### Kurallar

| Gösterim | Etki |
|---|---|
| `{söylenen\|yazılan}` | birincisini söyler, ikincisini yazar |
| `{metin}` çizgisiz | harfiyen kalır — işaret değil |
| boş taraf (`{\|metin}`, `{metin\|}`) | harfiyen kalır — hiçbir şey silinmez |
| `{` kapanmadan `}` | harfiyen kalır |
| hatalı işaret | metni asla yok etmez |

Yazılı biçim **dokunulmazdır**: aynı çekim, aynı yazım, aynı noktalama.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## AI modeli için kopyala-kullan istemi

VoxQueue'nun doğrudan kabul edeceği bir senaryo almak için herhangi bir asistanı yapıştırın.

```
VoxQueue içe aktarımı için bir `.txt` dosyası hazırla. Bir satır = bir replik.
Doğal Türkçe ile yaz. Konuşma sentezinin yanlış okuyacağı her yerde — yabancı özel
adlar, çok basamaklı sayılar, kısaltmalar, semboller, birimler — `{telaffuz|yazım}`
işaretini kullan, örneğin `{Eter|Aether}` veya `{iki bin yirmi dört|2024}`.
Sağdaki biçim, transkripte girmesi gereken şeyin tam kendisi olmalı ve özgün
çekimini korumalı. Sıradan sözcükleri, tek basamaklı sayıları veya doğal okunan
tarihleri işaretleme. Çevirme. Kaynak metindeki dilbilgisini düzeltme. Sonunda
telaffuzundan emin olmadığın tüm satırları listele.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Diğer diller: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
