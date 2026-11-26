$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if (!(Test-Path -LiteralPath '.env')) {
        $password = 'Tn!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'a9'
        [IO.File]::WriteAllText((Join-Path (Get-Location) '.env'), "MSSQL_SA_PASSWORD=$password" + [Environment]::NewLine)
    }
    $passwordLine = Get-Content -LiteralPath '.env' | Where-Object { $_.StartsWith('MSSQL_SA_PASSWORD=') } | Select-Object -First 1
    if (!$passwordLine) { throw 'The .env file must contain MSSQL_SA_PASSWORD.' }
    $password = $passwordLine.Substring('MSSQL_SA_PASSWORD='.Length)
    $connection = "Server=127.0.0.1,14333;Database=TimeNest_Demo;User Id=sa;Password=$password;Encrypt=True;TrustServerCertificate=True"
    @{ 'ConnectionStrings:TimeNest' = $connection } | ConvertTo-Json |
        dotnet user-secrets set --project api/TimeNest.Api
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure local user-secrets.' }
    docker compose up -d --wait --wait-timeout 180
    if ($LASTEXITCODE -ne 0) { throw 'SQL Server failed to start. Check Docker Desktop and docker compose logs database.' }
    dotnet run --project api/TimeNest.Api -- --seed-demo
    if ($LASTEXITCODE -ne 0) { throw 'Migration or demo seeding failed.' }
    Write-Host 'Local SQL Server is ready. Start the API with: dotnet run --project api/TimeNest.Api'
}
finally {
    Pop-Location
}
