[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

dotnet tool update --global dotnet-ef
dotnet ef database update --project MessengerAPI --startup-project MessengerAPI