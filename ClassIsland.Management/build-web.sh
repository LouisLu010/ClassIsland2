#!/usr/bin/env sh
set -eu
management_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
dotnet_command=${DOTNET_COMMAND:-dotnet}
"$dotnet_command" publish "$management_root/Browser/ClassIsland.Management.Browser.csproj" -c Debug
mkdir -p "$management_root/Server/wwwroot"
cp -R "$management_root/Browser/bin/Debug/net10.0/publish/wwwroot/." "$management_root/Server/wwwroot/"
