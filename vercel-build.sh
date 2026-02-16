#!/usr/bin/env bash
set -euo pipefail

curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

dotnet --info
dotnet publish Enigma/Enigma.Client/Enigma.Client.csproj -c Release
