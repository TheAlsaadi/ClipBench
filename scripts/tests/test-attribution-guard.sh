#!/usr/bin/env bash
# Tests for scripts/attribution-guard.sh.
# Runs anywhere with bash and git; CI runs it on ubuntu before it runs the guard itself.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
guard="$here/../attribution-guard.sh"
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

# text_case <name> <expected exit> <pull request title> <pull request body>
text_case() {
  PR_TITLE=$3 PR_BODY=$4 bash "$guard" >/dev/null 2>&1
  expect "$1" "$2" "$?"
}

new_repo() {
  git init -q -b main "$1"
}

# commit <repo> <message> [author name] [author email] [committer name] [committer email]
commit() {
  local repo=$1 message=$2
  local author_name=${3:-Owner} author_email=${4:-owner@example.invalid}
  local committer_name=${5:-$author_name} committer_email=${6:-$author_email}
  printf '%s\n' "$message" > "$tmp/message"
  GIT_AUTHOR_NAME=$author_name GIT_AUTHOR_EMAIL=$author_email \
    GIT_COMMITTER_NAME=$committer_name GIT_COMMITTER_EMAIL=$committer_email \
    git -C "$repo" -c commit.gpgsign=false -c core.hooksPath=/dev/null commit -q --allow-empty -F "$tmp/message"
}

# range_case <name> <expected exit> <repo> <base> <head>
range_case() {
  (cd "$3" && env -u PR_TITLE -u PR_BODY bash "$guard" "$4" "$5" >/dev/null 2>&1)
  expect "$1" "$2" "$?"
}

# --- pull request title and body ------------------------------------------------------------

text_case 'clean title and body' 0 'feat: add batch paste' $'Adds batch paste.\n\nCloses #12'
text_case 'empty title and body' 0 '' ''
text_case 'unicode text' 0 'feat: إضافة ميزة' $'يضيف هذا التغيير ميزة جديدة\n🚀'
text_case 'human co-author is allowed' 0 'fix: typo' $'Fix.\n\nCo-Authored-By: A Human <human@example.com>'
text_case 'mentioning an unrelated tool is allowed' 0 'docs: explain the cloud sync decision' 'No cloud sync in v1.'

text_case 'co-author trailer naming the tool (body)' 1 'feat: x' $'Body\n\nCo-Authored-By: Claude <noreply@anthropic.com>'
text_case 'co-author trailer naming the vendor (body)' 1 'feat: x' $'Body\n\nCo-Authored-By: Some Bot <bot@anthropic.com>'
text_case 'co-author trailer, upper case' 1 'feat: x' $'Body\n\nCO-AUTHORED-BY: CLAUDE <x@example.com>'
text_case 'co-author trailer, lower case and indented' 1 'feat: x' $'Body\n\n   co-authored-by: claude opus <x@example.com>'
text_case 'session trailer' 1 'feat: x' $'Body\n\nClaude-Session: https://example.invalid/abc'
text_case 'session trailer, lower case' 1 'feat: x' $'Body\n\nclaude-session: abc'
text_case 'generated-with footer' 1 'feat: x' $'Body\n\nGenerated with [Claude Code](https://example.invalid)'
text_case 'generated-with footer without link' 1 'feat: x' $'Body\n\nGENERATED WITH CLAUDE CODE'
text_case 'session link' 1 'feat: x' $'Body\n\nhttps://claude.ai/code/session_01ABC'
text_case 'attribution in the title' 1 'Co-Authored-By: Claude <x@example.com>' 'Body'
text_case 'attribution on the last line without a newline' 1 'feat: x' $'Body\nClaude-Session: x'

# --- hostile text must be treated as data ---------------------------------------------------

hostile=$'x $(touch "'"$tmp"$'/pwned1") `touch '"$tmp"$'/pwned2` ; touch '"$tmp"$'/pwned3 \' " <> | & \\ \\n %s %d ${HOME} -e -n --help'
text_case 'hostile text without attribution passes' 0 "$hostile" "$hostile"
text_case 'hostile text with attribution still fails' 1 "$hostile" "$hostile"$'\nClaude-Session: x'
text_case 'text that looks like an option' 0 '--help' $'-e\n-n\n--'
if [[ -e "$tmp/pwned1" || -e "$tmp/pwned2" || -e "$tmp/pwned3" ]]; then
  failed=$((failed + 1))
  printf 'FAIL: hostile text was executed by the shell\n'
else
  passed=$((passed + 1))
fi

# --- commit ranges -------------------------------------------------------------------------

repo="$tmp/clean"
new_repo "$repo"
commit "$repo" 'chore: base'
base=$(git -C "$repo" rev-parse HEAD)
commit "$repo" 'feat: first change'
commit "$repo" 'fix: second change'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'range: clean commits' 0 "$repo" "$base" "$head"

repo="$tmp/trailer-last"
new_repo "$repo"
commit "$repo" 'chore: base'
base=$(git -C "$repo" rev-parse HEAD)
commit "$repo" $'feat: change\n\nCo-Authored-By: Claude <noreply@anthropic.com>'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'range: attribution in the newest commit' 1 "$repo" "$base" "$head"

repo="$tmp/trailer-middle"
new_repo "$repo"
commit "$repo" 'chore: base'
base=$(git -C "$repo" rev-parse HEAD)
commit "$repo" 'feat: one'
commit "$repo" $'feat: two\n\nGenerated with [Claude Code](https://example.invalid)'
commit "$repo" 'feat: three'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'range: attribution in a middle commit' 1 "$repo" "$base" "$head"

repo="$tmp/trailer-before-base"
new_repo "$repo"
commit "$repo" $'chore: old\n\nCo-Authored-By: Claude <noreply@anthropic.com>'
base=$(git -C "$repo" rev-parse HEAD)
commit "$repo" 'feat: clean'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'range: attribution before the base is ignored' 0 "$repo" "$base" "$head"

repo="$tmp/author-name"
new_repo "$repo"
commit "$repo" 'chore: base'
base=$(git -C "$repo" rev-parse HEAD)
commit "$repo" 'feat: change' 'Claude' 'someone@example.invalid'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'range: tool as commit author' 1 "$repo" "$base" "$head"

repo="$tmp/committer-email"
new_repo "$repo"
commit "$repo" 'chore: base'
base=$(git -C "$repo" rev-parse HEAD)
commit "$repo" 'feat: change' 'Owner' 'owner@example.invalid' 'Owner' 'noreply@anthropic.com'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'range: vendor address as committer' 1 "$repo" "$base" "$head"

# A push that creates a branch has an all-zero "before" sha: only commits not on origin/main count.
repo="$tmp/new-branch"
new_repo "$repo"
commit "$repo" 'chore: base'
git -C "$repo" update-ref refs/remotes/origin/main HEAD
commit "$repo" 'feat: clean branch commit'
head=$(git -C "$repo" rev-parse HEAD)
zeros=0000000000000000000000000000000000000000
range_case 'new branch: clean commits' 0 "$repo" "$zeros" "$head"
commit "$repo" $'feat: tainted\n\nClaude-Session: x'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'new branch: attribution in a branch commit' 1 "$repo" "$zeros" "$head"

repo="$tmp/new-branch-old-taint"
new_repo "$repo"
commit "$repo" $'chore: base\n\nCo-Authored-By: Claude <noreply@anthropic.com>'
git -C "$repo" update-ref refs/remotes/origin/main HEAD
commit "$repo" 'feat: clean branch commit'
head=$(git -C "$repo" rev-parse HEAD)
range_case 'new branch: attribution already on origin/main is ignored' 0 "$repo" "$zeros" "$head"

# --- combined: clean commits but tainted pull request text ---------------------------------

repo="$tmp/combined"
new_repo "$repo"
commit "$repo" 'chore: base'
base=$(git -C "$repo" rev-parse HEAD)
commit "$repo" 'feat: clean'
head=$(git -C "$repo" rev-parse HEAD)
(cd "$repo" && PR_TITLE='feat: clean' PR_BODY=$'Body\nClaude-Session: x' bash "$guard" "$base" "$head" >/dev/null 2>&1)
expect 'combined: clean commits, tainted pull request body' 1 "$?"

printf '%d passed, %d failed\n' "$passed" "$failed"
[[ $failed -eq 0 ]]
