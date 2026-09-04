#!/usr/bin/env bash
# Runs a build/test command, echoes its output, and records any errors into
# $GITHUB_WORKSPACE/errors.log. Exits non-zero when the command fails so the
# workflow step (and therefore the job) is marked as failed.
#
# Usage: run-step.sh "<error title>" <command> [args...]

set -uo pipefail

title="$1"
shift

output=$( "$@" 2>&1 )
status=$?

printf '%s\n' "$output"

errors=$( printf '%s\n' "$output" | awk '/: error /' )

if [ -z "$errors" ] && [ "$status" -ne 0 ]; then
  errors="$* exited with code $status"
fi

if [ -n "$errors" ]; then
  printf '%s:\n%s\n' "$title" "$errors" >> "${GITHUB_WORKSPACE:-.}/errors.log"
  exit 1
fi

exit 0