# Prints one UUID per line (ThixFred: Enter copies the chosen one). The first argument is how many (1-20, default 5).
param([string]$Query)

$count = 5
if ($Query -match '^\d+$') { $count = [Math]::Min(20, [Math]::Max(1, [int]$Query)) }
1..$count | ForEach-Object { [guid]::NewGuid().ToString() }
