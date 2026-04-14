#!/usr/bin/env bash
# scripts/setup-hooks.sh
#
# One-time setup: configures git to use .husky/ as the hooks directory.
# Run this after cloning or when first working in the repository.
#
# Usage:
#   bash scripts/setup-hooks.sh
#
# What it does:
#   1. Sets git core.hooksPath to .husky/
#   2. Makes .husky/pre-commit and .husky/pre-push executable
#   3. Restores dotnet local tools (csharpier, roslynator, etc.)

set -euo pipefail

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
cd "$REPO_ROOT"

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
pass() { echo -e "${GREEN}✔${NC} $1"; }
step() { echo -e "${YELLOW}▶${NC} $1"; }

step "Configuring git hooks path → .husky/"
git config core.hooksPath .husky
pass "git config core.hooksPath = .husky"

step "Making hook scripts executable"
chmod +x .husky/pre-commit .husky/pre-push
pass "chmod +x .husky/pre-commit .husky/pre-push"

# Restore dotnet local tools if a manifest exists
if [ -f ".config/dotnet-tools.json" ]; then
  step "Restoring dotnet local tools"
  dotnet tool restore
  pass "dotnet tool restore complete"
else
  echo -e "${YELLOW}ℹ${NC}  No .config/dotnet-tools.json found."
  echo "   Consider creating one to pin tool versions for the team:"
  echo "   dotnet new tool-manifest"
  echo "   dotnet tool install csharpier"
  echo "   dotnet tool install roslynator.dotnet.cli"
fi

echo ""
echo -e "${GREEN}✔ Husky hooks installed.${NC}"
echo "  Pre-commit: csharpier check + roslynator errors + unit tests + semgrep + shellcheck"
echo "  Pre-push:   full test suite + coverage threshold + roslynator warnings + semgrep"
echo ""
echo "  To skip a hook in an emergency:  git commit --no-verify / git push --no-verify"
