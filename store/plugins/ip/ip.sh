#!/bin/bash
# The public IPv4 address and the local ones as ThixFred results (JSON); Enter copies.
items=()
public=$(curl -s --max-time 4 https://api.ipify.org)
if [ -n "$public" ]; then items+=("{\"title\":\"$public\",\"subtitle\":\"Öffentlich (api.ipify.org)\",\"arg\":\"$public\"}"); fi
for interface in $(ifconfig -l); do
  address=$(ipconfig getifaddr "$interface" 2>/dev/null)
  if [ -n "$address" ]; then items+=("{\"title\":\"$address\",\"subtitle\":\"Lokal · $interface\",\"arg\":\"$address\"}"); fi
done
(IFS=,; echo "{\"items\":[${items[*]}]}")
