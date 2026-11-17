# A Unix time as a date, or now in several forms (ThixFred results, JSON); Enter copies.
param([string]$Query)

$culture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
if ($Query -match '^\d{9,13}$') {
  $value = [long]$Query
  $moment = if ($Query.Length -gt 10) { [DateTimeOffset]::FromUnixTimeMilliseconds($value) } else { [DateTimeOffset]::FromUnixTimeSeconds($value) }
} else {
  $moment = [DateTimeOffset]::Now
}
$local = $moment.ToLocalTime()
$week = $culture.Calendar.GetWeekOfYear($local.DateTime, [Globalization.CalendarWeekRule]::FirstFourDayWeek, [DayOfWeek]::Monday)
$items = @(
  @{ title = $local.ToString('dddd, dd.MM.yyyy HH:mm:ss', $culture); subtitle = 'Ortszeit' },
  @{ title = [string]$moment.ToUnixTimeSeconds(); subtitle = 'Unix-Zeit (Sekunden)' },
  @{ title = $moment.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); subtitle = 'ISO 8601 (UTC)' },
  @{ title = "KW $week"; subtitle = 'Kalenderwoche' }
)
@{ items = $items } | ConvertTo-Json -Depth 4 -Compress
