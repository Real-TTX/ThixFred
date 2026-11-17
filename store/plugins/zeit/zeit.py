#!/usr/bin/env python3
"""A Unix time as a date, or now in several forms (ThixFred results, JSON); Enter copies."""
import datetime
import json
import sys

query = sys.argv[1] if len(sys.argv) > 1 else ""
if query.isdigit() and 9 <= len(query) <= 13:
    seconds = int(query) / (1000 if len(query) > 10 else 1)
    moment = datetime.datetime.fromtimestamp(seconds, datetime.timezone.utc)
else:
    moment = datetime.datetime.now(datetime.timezone.utc)
local = moment.astimezone()
items = [
    {"title": local.strftime("%d.%m.%Y %H:%M:%S"), "subtitle": "Ortszeit"},
    {"title": str(int(moment.timestamp())), "subtitle": "Unix-Zeit (Sekunden)"},
    {"title": moment.strftime("%Y-%m-%dT%H:%M:%SZ"), "subtitle": "ISO 8601 (UTC)"},
    {"title": f"KW {local.isocalendar()[1]}", "subtitle": "Kalenderwoche (ISO)"},
]
print(json.dumps({"items": items}, ensure_ascii=False))
