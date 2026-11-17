# The text in Base64 and, when it is Base64, decoded (ThixFred results, JSON); Enter copies.
param([string]$Query)

if ($Query.Length -eq 0) {
  @{ items = @(@{ title = 'Text eingeben: b64 Hallo'; subtitle = 'Base64' }) } | ConvertTo-Json -Depth 4 -Compress
  exit
}

$items = @(@{ title = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Query)); subtitle = 'Kodiert' })
try {
  $decoded = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Query))
  $items += @{ title = $decoded; subtitle = 'Dekodiert' }
} catch {
  # not Base64: only the encoded form
}
@{ items = $items } | ConvertTo-Json -Depth 4 -Compress
