#!/bin/bash
# Prints one UUID per line (ThixFred: Enter copies the chosen one). The first argument is how many (1-20, default 5).
count=5
if [[ "$1" =~ ^[0-9]+$ ]]; then count=$(( $1 < 1 ? 1 : ($1 > 20 ? 20 : $1) )); fi
for ((i = 0; i < count; i++)); do uuidgen | tr '[:upper:]' '[:lower:]'; done
