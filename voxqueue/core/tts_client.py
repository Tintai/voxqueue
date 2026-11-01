"""Minimal client for an OpenAI-compatible /v1/audio endpoint.

Written against the protocol rather than one server: it works with VoiceStudio,
the Aether voice hub, Piper or anything else that speaks the same shape. That
matters because the same UI has to drive OmniVoice on one machine and Piper on
another.
"""

from __future__ import annotations

import json
import socket
import urllib.error
import urllib.request
from dataclasses import dataclass


class TTSError(Exception):
    """Anything that stopped a generation, with a message fit for the UI."""


@dataclass
class VoiceInfo:
    id: str
    name: str = ""

    def label(self) -> str:
        return self.name or self.id


class TTSClient:
    def __init__(self, base_url: str, api_key: str = "", timeout: int = 900) -> None:
        base = (base_url or "").strip().rstrip("/")
        # Accept "host:port", "http://host:port" and "http://host:port/v1".
        if base and not base.startswith(("http://", "https://")):
            base = "http://" + base
        if base.endswith("/v1"):
            base = base[:-3]
        self.base = base
        self.key = (api_key or "").strip()
        self.timeout = timeout

    # ------------------------------------------------------------ helpers
    def _req(self, path: str, data: bytes | None = None, method: str = "GET"):
        url = f"{self.base}{path}"
        req = urllib.request.Request(url, data=data, method=method)
        if data is not None:
            req.add_header("Content-Type", "application/json")
        if self.key:
            req.add_header("Authorization", f"Bearer {self.key}")
        return urllib.request.urlopen(req, timeout=self.timeout)

    def _error(self, exc: Exception) -> str:
        if isinstance(exc, urllib.error.HTTPError):
            body = ""
            try:
                body = exc.read().decode("utf-8", "replace")[:300]
            except Exception:
                pass
            if exc.code == 401:
                return "HTTP 401 — " + (body or "invalid or missing API key")
            return f"HTTP {exc.code} — {body or exc.reason}"
        if isinstance(exc, urllib.error.URLError):
            reason = getattr(exc, "reason", exc)
            if isinstance(reason, socket.timeout):
                return "timeout on connect"
            return f"cannot reach {self.base} ({reason})"
        if isinstance(exc, socket.timeout):
            return "timeout"
        return f"{type(exc).__name__}: {exc}"

    # -------------------------------------------------------------- calls
    def ping(self) -> tuple[bool, str]:
        """Cheap reachability probe used by the connection panel."""
        for path in ("/v1/audio/voices", "/v1/models", "/health"):
            try:
                with self._req(path) as r:
                    r.read(2048)
                return True, f"{self.base}{path}"
            except urllib.error.HTTPError as e:
                # A 401 still proves the server is up and speaking the protocol.
                if e.code in (401, 403):
                    return True, f"{self.base}{path} (auth required)"
            except Exception as e:
                last = self._error(e)
                continue
        return False, last if "last" in dir() else "unreachable"

    def voices(self) -> list[VoiceInfo]:
        with self._req("/v1/audio/voices") as r:
            payload = json.loads(r.read().decode("utf-8") or "{}")
        out: list[VoiceInfo] = []
        for v in payload.get("voices") or []:
            if isinstance(v, dict):
                vid = str(v.get("id") or v.get("voice_id") or "").strip()
                nm = str(v.get("name") or "").strip()
            else:
                vid, nm = str(v).strip(), ""
            if vid:
                out.append(VoiceInfo(id=vid, name=nm))
        return out

    def speak(self, text: str, voice: str, fmt: str = "wav") -> bytes:
        """One utterance. Returns the encoded audio bytes."""
        if not text.strip():
            raise TTSError("empty text")
        if not voice:
            raise TTSError("no voice selected")
        body = json.dumps({
            "model": "tts-1",
            "voice": voice,
            "input": text,
            "response_format": fmt,
        }).encode("utf-8")
        try:
            with self._req("/v1/audio/speech", data=body, method="POST") as r:
                data = r.read()
        except Exception as e:
            raise TTSError(self._error(e)) from e
        if not data or len(data) < 128:
            raise TTSError("server returned an empty or truncated response")
        return data
