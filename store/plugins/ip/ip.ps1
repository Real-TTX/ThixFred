# The local IPv4 addresses and the public one as ThixFred results (JSON); Enter copies.
# .NET instead of Get-NetIPAddress (which loads CIM and takes a second or more); TLS 1.2 for Windows PowerShell 5.1.
$items = @()
foreach ($interface in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
  if ($interface.OperationalStatus -ne 'Up' -or $interface.NetworkInterfaceType -eq 'Loopback') { continue }
  foreach ($address in $interface.GetIPProperties().UnicastAddresses) {
    if ($address.Address.AddressFamily -eq 'InterNetwork') {
      $ip = $address.Address.ToString()
      $items += @{ title = $ip; subtitle = "Lokal · $($interface.Name)"; arg = $ip; action = 'copy' }
    }
  }
}

try {
  [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
  $request = [Net.WebRequest]::Create('https://api.ipify.org')
  $request.Timeout = 3000
  $response = $request.GetResponse()
  $public = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd().Trim()
  $response.Close()
  $items = @(@{ title = $public; subtitle = 'Öffentlich (api.ipify.org)'; arg = $public; action = 'copy' }) + $items
} catch {
  $items += @{ title = 'Öffentliche IP nicht erreichbar'; subtitle = 'Keine Verbindung zu api.ipify.org' }
}

@{ items = $items } | ConvertTo-Json -Depth 4 -Compress
