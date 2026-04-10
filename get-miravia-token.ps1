# get-miravia-token.ps1
# Intercambia el authorization code de Miravia por AccessToken + RefreshToken

$appKey    = "512834"
$appSecret = "w2Bamaj5ngQb41f7Sd5pb8AjFKFHD5gc"
$code      = "3_512834_FOJJWRrrWTbf0BU67vox9G1W12"
$apiName   = "/auth/token/create"
$serverUrl = "https://api.miravia.es/rest"

# Timestamp en milisegundos UTC
$timestamp  = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds().ToString()
$partnerId  = "iop-sdk-net-20180508"
$signMethod = "sha256"

# 1. Parámetros a firmar (sin 'sign')
$params = @{
    "app_key"     = $appKey
    "code"        = $code
    "partner_id"  = $partnerId
    "sign_method" = $signMethod
    "timestamp"   = $timestamp
}

# 2. Ordenar y construir string: apiName + key1val1 + key2val2 ...
$sortedKeys  = $params.Keys | Sort-Object
$stringToSign = $apiName
foreach ($key in $sortedKeys) {
    if ($params[$key]) { $stringToSign += $key + $params[$key] }
}

Write-Host "String to sign: $stringToSign"

# 3. HMAC-SHA256
$hmac      = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key  = [System.Text.Encoding]::UTF8.GetBytes($appSecret)
$signBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($stringToSign))
$sign      = ($signBytes | ForEach-Object { $_.ToString("X2") }) -join ""

# 4. Añadir sign y construir URL
$params["sign"] = $sign

$queryString = ($params.GetEnumerator() | Sort-Object Key | ForEach-Object {
    "$($_.Key)=$([Uri]::EscapeDataString($_.Value))"
}) -join "&"

$url = "$serverUrl$apiName`?$queryString"
Write-Host "`nLlamando a: $url`n"

# 5. Llamar a la API (POST con body form-urlencoded)
try {
    $response = Invoke-RestMethod -Uri "$serverUrl$apiName" -Method POST `
        -ContentType "application/x-www-form-urlencoded;charset=utf-8" `
        -Body $queryString
    Write-Host "=== RESPUESTA ===" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 10 | Write-Host

    $accessToken  = $response.access_token
    $refreshToken = $response.refresh_token

    if ($accessToken) {
        Write-Host "`n✅ AccessToken:  $accessToken"  -ForegroundColor Cyan
        Write-Host "✅ RefreshToken: $refreshToken" -ForegroundColor Cyan
    }
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ErrorDetails.Message
}
