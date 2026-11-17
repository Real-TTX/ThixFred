# Five random passwords from a cryptographic generator, one per line. The first argument is the length (8-128, default 20).
param([string]$Query)

$length = 20
if ($Query -match '^\d+$') { $length = [Math]::Min(128, [Math]::Max(8, [int]$Query)) }
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#$%&*+-=?@_'.ToCharArray()
$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
1..5 | ForEach-Object {
  $bytes = New-Object byte[] $length
  $random.GetBytes($bytes)
  -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}
