"""VoxQueue — batch TTS front-end for OpenAI-compatible speech servers.

Modes
-----
train   one line of text -> one WAV + one metadata.csv row (LJSpeech layout)
dub     "CHARACTER: line" -> one WAV per line, routed to that character's voice
bulk    marker-separated blocks -> one WAV + one .txt transcript per block
"""

# Frozen. The reference implementation is the C#/WinForms build (publish/windows),
# which is several versions ahead: phonetic {spoken|written} markers, the
# Pronunciation column, .vqproj projects and metadata-with-pronunciation are not
# implemented here. Kept as a working Linux starting point, not as a peer.
__version__ = "1.27.3"
APP_NAME = "VoxQueue"
ORG_NAME = "Aether"
