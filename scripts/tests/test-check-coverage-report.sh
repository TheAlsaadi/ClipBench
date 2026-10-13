#!/usr/bin/env bash
# Tests for scripts/check-coverage-report.sh.
# Runs anywhere with bash; CI runs it before it relies on the check.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
check="$here/../check-coverage-report.sh"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

passed=0
failed=0

# expect <name> <expected exit code> <actual exit code>
expect() {
  if [[ $2 == "$3" ]]; then
    passed=$((passed + 1))
  else
    failed=$((failed + 1))
    printf 'FAIL: %s (expected exit %s, got %s)\n' "$1" "$2" "$3"
  fi
}

# report <directory> <attributes of the root element>
# Writes a minimal Cobertura report the way coverlet names it.
report() {
  mkdir -p "$1"
  printf '<?xml version="1.0" encoding="utf-8"?>\n<coverage %s version="1.9">\n  <packages />\n</coverage>\n' "$2" \
    > "$1/coverage.cobertura.131026115343250.xml"
}

# run_case <name> <expected exit> <directory> <floor>
run_case() {
  bash "$check" "$3" "$4" >/dev/null 2>&1
  expect "$1" "$2" "$?"
}

# --- reports that measured code ---------------------------------------------------------------

report "$tmp/real" 'line-rate="0.9876" branch-rate="0.9485" timestamp="1791892423" lines-covered="722" lines-valid="731" branches-covered="590" branches-valid="622"'
run_case 'real coverlet header, floor 90' 0 "$tmp/real" 90

report "$tmp/one" 'lines-valid="1"'
run_case 'one measured line' 0 "$tmp/one" 90

report "$tmp/hundred" 'lines-valid="100"'
run_case 'lines-valid=100 is not mistaken for zero' 0 "$tmp/hundred" 90

report "$tmp/nested/deeper" 'lines-valid="40"'
run_case 'report in a sub folder' 0 "$tmp/nested" 90

# --- more than one report in the folder: the newest one counts ---------------------------------

# coverlet puts the time in the file name, so a later run sorts after an earlier one.
report_named() {
  mkdir -p "$1"
  printf '<?xml version="1.0"?>\n<coverage lines-valid="%s" version="1.9">\n</coverage>\n' "$3" > "$1/$2"
}

report_named "$tmp/stale_then_good" 'coverage.cobertura.131026100000000.xml' 0
report_named "$tmp/stale_then_good" 'coverage.cobertura.131026200000000.xml' 731
run_case 'old empty report, newer good report' 0 "$tmp/stale_then_good" 90

report_named "$tmp/good_then_empty" 'coverage.cobertura.131026100000000.xml' 731
report_named "$tmp/good_then_empty" 'coverage.cobertura.131026200000000.xml' 0
run_case 'old good report, newer empty report' 1 "$tmp/good_then_empty" 90

# --- reports that measured nothing ------------------------------------------------------------

report "$tmp/empty" 'line-rate="1" branch-rate="1" lines-covered="0" lines-valid="0"'
run_case 'no lines measured, floor enforced' 1 "$tmp/empty" 90
run_case 'no lines measured, floor of 1' 1 "$tmp/empty" 1
run_case 'no lines measured, no floor' 0 "$tmp/empty" 0

report "$tmp/noattr" 'line-rate="1"'
run_case 'report without lines-valid' 1 "$tmp/noattr" 90

mkdir -p "$tmp/norport"
run_case 'no report at all, floor enforced' 1 "$tmp/norport" 90
run_case 'no report at all, no floor' 0 "$tmp/norport" 0
run_case 'results folder missing' 1 "$tmp/does-not-exist" 90

# --- bad arguments ----------------------------------------------------------------------------

run_case 'floor is not a number' 2 "$tmp/real" 'ninety'
run_case 'floor is negative' 2 "$tmp/real" '-5'
run_case 'floor is empty' 2 "$tmp/real" ''

bash "$check" >/dev/null 2>&1
expect 'no arguments' 2 "$?"
bash "$check" "$tmp/real" >/dev/null 2>&1
expect 'one argument' 2 "$?"
bash "$check" "$tmp/real" 90 extra >/dev/null 2>&1
expect 'three arguments' 2 "$?"

printf '%s passed, %s failed\n' "$passed" "$failed"
((failed == 0))
