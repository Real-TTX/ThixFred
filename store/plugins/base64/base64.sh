#!/bin/bash
# The text in Base64 and, when it is Base64, decoded (one result per line: Enter copies it).
if [ -z "$1" ]; then echo "Text eingeben: b64 Hallo"; exit 0; fi
printf '%s' "$1" | base64
decoded=$(printf '%s' "$1" | base64 -D 2>/dev/null) && [ -n "$decoded" ] && printf '%s\n' "$decoded"
exit 0
