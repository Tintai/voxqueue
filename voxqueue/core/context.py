"""Shared application context: settings, translations, client and voice list.

Tabs talk to this instead of to each other, so no tab needs to know which other
tab exists. Voice refresh is a single call that every tab reacts to.
"""

from __future__ import annotations

from .config import CONFIG, Config
from .tts_client import TTSClient, VoiceInfo
from ..i18n import I18N


class Context:
    def __init__(self) -> None:
        self.cfg: Config = CONFIG
        self.tr: I18N = I18N(self.cfg.get("language", "system"))
        self.voices: list[VoiceInfo] = []
        self._client: TTSClient | None = None
        self.theme_name: str = str(self.cfg.get("theme") or "dark")
        self._listeners: list = []

    # ------------------------------------------------------------------ client
    @property
    def client(self) -> TTSClient:
        key = (self.cfg.get("base_url"), self.cfg.get("api_key"))
        if self._client is None or getattr(self, "_client_key", None) != key:
            self._client = TTSClient(self.cfg.get("base_url"),
                                     self.cfg.get("api_key") or "")
            self._client_key = key
        return self._client

    def invalidate_client(self) -> None:
        self._client = None

    # ------------------------------------------------------------------ voices
    def refresh_voices(self) -> tuple[bool, str]:
        """Reload the voice list. Returns (ok, message)."""
        try:
            voices = self.client.voices()
        except Exception as e:
            return False, str(e)
        if not voices:
            return False, "server returned an empty voice list"
        self.voices = voices
        self.notify()
        return True, f"{len(voices)} voices"

    def voice_ids(self) -> list[str]:
        return [v.id for v in self.voices]

    def voice_label(self, vid: str) -> str:
        for v in self.voices:
            if v.id == vid:
                return v.label()
        return vid or "—"

    # ------------------------------------------------------------- observers
    def subscribe(self, fn) -> None:
        if fn not in self._listeners:
            self._listeners.append(fn)

    def notify(self) -> None:
        for fn in list(self._listeners):
            try:
                fn()
            except Exception:
                pass

    # ------------------------------------------------------------------- theme
    def palette_name(self) -> str:
        if self.theme_name == "system":
            try:
                from PySide6.QtWidgets import QApplication
                hints = QApplication.styleHints()
                dark = hints.colorScheme().name.lower() == "dark"
                return "dark" if dark else "light"
            except Exception:
                return "dark"
        return self.theme_name if self.theme_name in ("dark", "light") else "dark"
