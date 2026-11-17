#!/bin/bash
# Five random passwords from /dev/urandom, one per line. The first argument is the length (8-128, default 20).
length=20
if [[ "$1" =~ ^[0-9]+$ ]]; then length=$(( $1 < 8 ? 8 : ($1 > 128 ? 128 : $1) )); fi
for i in 1 2 3 4 5; do
  LC_ALL=C tr -dc 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#$%&*+=?@_-' < /dev/urandom | head -c "$length"
  echo
done
