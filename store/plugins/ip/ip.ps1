# The public IPv4 address and the local ones as ThixFred results (JSON); Enter copies.
$items = @()
try {
  $public = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=json' -TimeoutSec 4).ip
  $items += @{ title = $public; subtitle = 'Öffentlich (api.ipify.org)'; arg = $public; action = 'copy' }
} catch {
  $items += @{ title = 'Öffentliche IP nicht erreichbar'; subtitle = 'Keine Verbindung zu api.ipify.org' }
}
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
  Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } |
  ForEach-Object { $items += @{ title = $_.IPAddress; subtitle = "Lokal · $($_.InterfaceAlias)"; arg = $_.IPAddress; action = 'copy' } }
@{ items = $items } | ConvertTo-Json -Depth 4 -Compress
