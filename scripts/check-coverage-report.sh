#!/usr/bin/env bash
# Coverage report check.
#
# A coverage run that measures nothing passes every floor: an assembly with no code reports 100%
# coverage, and so does a run whose --coverlet-include filter matches nothing. For an enforced floor this
# script therefore also requires the Cobertura report to show at least one measured line.
#
# Usage: check-coverage-report.sh <results directory> <floor>
#   floor  The line coverage floor that was enforced for this run. 0 means the assembly has no code
#          yet and nothing is checked.
#
# Exit status: 0 when the report is acceptable, 1 when it is missing or measured no lines, 2 for bad arguments.
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo 'Usage: check-coverage-report.sh <results directory> <floor>' >&2
  exit 2
fi

directory=$1
floor=$2

if ! [[ $floor =~ ^[0-9]+$ ]]; then
  echo "The floor must be a whole number, got '$floor'." >&2
  exit 2
fi

if ((floor == 0)); then
  echo "No floor is enforced for $directory yet; nothing to check."
  exit 0
fi

# The newest report counts: coverlet puts the time in the file name, and an older run may still be in the folder.
report=$(find "$directory" -name 'coverage.cobertura*.xml' 2>/dev/null | sort | tail -n 1 || true)
if [[ -z $report ]]; then
  echo "No Cobertura report was found in $directory." >&2
  exit 1
fi

# The root element carries the totals, for example lines-valid="731".
valid=$(grep -o -m 1 'lines-valid="[0-9]*"' "$report" | head -n 1 | grep -o '[0-9]*' || true)
if [[ -z $valid ]]; then
  echo "The report $report has no lines-valid attribute." >&2
  exit 1
fi

if ((valid == 0)); then
  echo "The report $report measured no lines, so the $floor% floor proves nothing. Check the --coverlet-include filter." >&2
  exit 1
fi

echo "$report measured $valid lines."
