# Creates local PostgreSQL database for ContextMemory (requires psql in PATH)
param(
    [string]$DbHost = "localhost",
    [int]$Port = 5432,
    [string]$Database = "contextmemory",
    [string]$Username = "postgres"
)

$ErrorActionPreference = "Stop"

Write-Host "Creating database '$Database' on ${DbHost}:${Port} (user: $Username)..."
$env:PGPASSWORD = Read-Host "PostgreSQL password for $Username"

$exists = psql -h $DbHost -p $Port -U $Username -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$Database'"
if ($exists -eq "1") {
    Write-Host "Database '$Database' already exists."
} else {
    psql -h $DbHost -p $Port -U $Username -d postgres -c "CREATE DATABASE $Database;"
    Write-Host "Database '$Database' created."
}

Write-Host "Update ConnectionStrings:ContextMemory in src/ContextMemory.Api/appsettings.json with your password."
