#!/usr/bin/env bash
# The text of the MSI's license page: NOTICE.txt (with the BASS terms) and the Apache 2.0 license, as RTF. vpk would
# read a .txt as Markdown and mangle it. Both files must stay plain ASCII. Usage, from the repo root: license-rtf.sh <out.rtf>
set -euo pipefail
{
  printf '%s\n' '{\rtf1\ansi{\fonttbl{\f0 Segoe UI;}}\f0\fs16'
  sed 's/\r$//; s/[\\{}]/\\&/g; s/$/\\par/' NOTICE.txt LICENSE.txt
  printf '%s\n' '}'
} > "$1"
