[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    [Parameter(Mandatory = $false)]
    [switch]$Apply
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Name))
{
    throw "Укажите имя миграции, например: InitialSchema"
}

dotnet tool update --global dotnet-ef
dotnet ef migrations add $Name --project MessengerAPI --startup-project MessengerAPI

if ($Apply)
{
    dotnet ef database update --project MessengerAPI --startup-project MessengerAPI
}
