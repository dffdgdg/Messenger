# run-wsl-client.ps1
$projectPath = "/mnt/c/Users/laptop/OneDrive/Документы/Projects/Messenger/MessengerDesktop"

wsl -e bash -c "cd $projectPath && MESSENGER_ENV=Wsl dotnet run"