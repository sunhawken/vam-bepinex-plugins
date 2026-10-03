#!/bin/sh
# usage: deploy.sh <live dll name> [built dll name]  - backs up the live plugin once (.bak) then copies the fresh build over it
live="T:/New folder/BepInEx/plugins/$1"
src="$(dirname "$0")/../../artifacts/Release/${2:-$1}"
[ -f "$live.bak" ] || cp "$live" "$live.bak"
cp "$src" "$live" && echo "deployed $1"
