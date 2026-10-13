#!/usr/bin/env bash
# Attribution guard.
#
# Fails when a commit message, a commit author or committer, the pull request title or the
# pull request body credits an AI tool as an author or co-author. The repository owner is the
# sole author; see the contributing guide.
#
# Usage: attribution-guard.sh [BASE [HEAD]]
#   BASE  Commits in BASE..HEAD are scanned. An all-zero BASE (a push that creates a branch), or
#         a BASE that is not in the clone (after a force push), scans every commit that is not
#         on origin/main instead: that can only scan more commits, never fewer. Omit BASE to
#         scan only the pull request text.
#   HEAD  Defaults to HEAD.
#
# Environment: PR_TITLE and PR_BODY hold the pull request text. They arrive through the
# environment and are only ever read as data; nothing in this script evaluates them.
#
# The script fails closed: if git cannot resolve the range, it exits non-zero.
set -euo pipefail

# Case-insensitive extended regular expressions, one per attribution form.
readonly PATTERNS=(
  'Co-Authored-By:.*(claude|anthropic)'
  'Claude-Session:'
  'Generated with \[?Claude Code'
  'claude\.ai/code/session_'
)

# Author and committer names and addresses must not name the tool or its vendor.
readonly IDENTITY_PATTERN='claude|anthropic'

found=0

# report <label> <line number> <line>
# Every echoed line is indented so text taken from a commit can never start with "::" and be
# read by the CI runner as a workflow command.
report() {
  printf 'Attribution found in %s (line %s):\n    | %s\n' "$1" "$2" "$3"
}

# scan_text <label> <text>
scan_text() {
  local label=$1 text=$2 pattern number line
  for pattern in "${PATTERNS[@]}"; do
    while IFS=: read -r number line; do
      report "$label" "$number" "$line"
      found=1
    done < <(printf '%s\n' "$text" | grep -a -i -n -E -- "$pattern" || true)
  done
}

# scan_identity <label> <author and committer identities, one per line>
scan_identity() {
  local label=$1 identities=$2 number line
  while IFS=: read -r number line; do
    report "$label author or committer" "$number" "$line"
    found=1
  done < <(printf '%s\n' "$identities" | grep -a -i -n -E -- "$IDENTITY_PATTERN" || true)
}

base=${1:-}
head=${2:-HEAD}

if [[ -n $base ]]; then
  if [[ $base =~ ^0+$ ]] || ! git cat-file -e "${base}^{commit}" 2> /dev/null; then
    if git rev-parse --verify --quiet refs/remotes/origin/main > /dev/null; then
      range=("$head" --not refs/remotes/origin/main)
    else
      range=("$head")
    fi
  else
    range=("$base..$head")
  fi

  # Assignments (not process substitution) so a git failure stops the script.
  commits=$(git rev-list "${range[@]}")
  while IFS= read -r sha; do
    [[ -n $sha ]] || continue
    message=$(git log -1 --format=%B "$sha")
    identities=$(git log -1 --format='%an <%ae>%n%cn <%ce>' "$sha")
    scan_text "commit ${sha:0:12}" "$message"
    scan_identity "commit ${sha:0:12}" "$identities"
  done <<< "$commits"
fi

scan_text 'pull request title' "${PR_TITLE:-}"
scan_text 'pull request body' "${PR_BODY:-}"

if ((found)); then
  printf '\nAttribution to AI tools is not allowed in commits or pull requests.\n' >&2
  printf 'Remove the lines above and push again.\n' >&2
  exit 1
fi

printf 'No attribution found.\n'
