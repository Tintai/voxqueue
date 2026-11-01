# VoxQueue — 한국어

OpenAI 호환 `/v1/audio` 서버(Piper, VoiceStudio, Aether Voice Hub)를 위한 일괄 음성 합성.

---

## 빠른 시작

**Windows** — `publish/windows/VoxQueue.exe`를 내려받아 실행하세요. 설치할 것이 없습니다.

**Linux** — `publish/linux/VoxQueue`, 또는 소스에서:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **설정 → 연결** — 서버 주소, 기본값 `http://127.0.0.1:3900`
2. **새로고침** — 음성 목록을 불러옵니다
3. 모드를 고르고 텍스트를 붙여넣습니다 (또는 `.txt`를 입력란에 끌어다 놓습니다)
4. **텍스트 분석** — 구문 분석기가 찾은 대사 수를 보여줍니다
5. **시작**

각 모드의 예제 파일은 `samples/`에 있습니다.

---

## 세 가지 모드

모드는 **한 가지만** 결정합니다: 텍스트를 대사로 나누는 방식과 파일 이름 규칙.

| 모드 | 입력 | 출력 |
|---|---|---|
| **학습** | 한 줄 = 한 대사 | `wav/0000.wav` + `metadata.csv` |
| **더빙** | `캐릭터: 대사` | `voices/캐릭터/캐릭터_0000_2.41s.wav` |
| **배치** | `###`로 구분된 블록 | `wav/0000.wav` + `0000.txt` |

---

## 발음 표시

TTS 모델은 철자가 아니라 글자를 읽습니다. `Aether`는 "A-e-ther"가 됩니다. `2024`는 철자로 읽힙니다. 숫자는 기본형으로만 풀리므로 어형이 바뀐 형태는 모두 틀립니다. 엔진에는 발음 표기를, 전사에는 올바른 철자를 넘기세요:

```
{Eter|Aether} 온라인입니다.
{이천이십사|2024}년.
{피엠|PM}이 보고서를 보냈습니다.
```

- **첫 번째** 형식이 TTS 엔진으로 갑니다 — 실제로 읽히는 것
- **두 번째** 형식이 `metadata.csv`와 `.txt` 전사로 갑니다

### 규칙

| 표기 | 효과 |
|---|---|
| `{발음\|표기}` | 첫 번째를 읽고 두 번째를 씁니다 |
| 세로줄 없는 `{텍스트}` | 그대로 남습니다 — 표시가 아님 |
| 빈 쪽 (`{\|텍스트}`, `{텍스트\|}`) | 그대로 남습니다 — 아무것도 삭제되지 않음 |
| `}` 없는 `{` | 그대로 남습니다 |
| 잘못된 표시 | 텍스트를 절대 훼손하지 않음 |

표기 형식은 **불가침**입니다: 같은 어형, 같은 철자, 같은 문장부호.

확정된 단어 목록은 언어마다 다릅니다 — 참조 음성을 들으며 작성하세요.

---

## AI 모델용 복사 가능한 프롬프트

아무 어시스턴트에 붙여넣으면 VoxQueue가 그대로 받아들이는 스크립트를 얻습니다.

```
VoxQueue 가져오기용 `.txt` 파일을 만들어 주세요. 한 줄 = 한 대사입니다. 자연스러
운 한국어로 작성하세요. 음성 합성이 잘못 읽을 부분 — 외래 고유명사, 여러 자리
숫자, 약어, 기호, 단위 — 에는 `{발음|표기}` 표시를 사용하세요. 예: `{Eter|Aether}`,
`{이천이십사|2024}`. 오른쪽 표기는 전사에 남아야 할 것과 정확히 같아야 하며 원래
의 어형을 유지해야 합니다. 일반 단어, 한 자리 숫자, 자연스럽게 읽히는 날짜에는
표시하지 마세요. 번역하지 마세요. 원문의 문법을 고치지 마세요. 마지막에 발음이
확실하지 않았던 줄을 모두 나열하세요.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

다른 언어: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
