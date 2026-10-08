#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
test_dir=$(mktemp -d)
trap 'rm -rf "$test_dir"' EXIT
${FOUNDATION_MCS:-mcs} -out:"$test_dir/foundation-tests.exe" Assets/Foundation/Runtime/FoundationModel.cs Assets/Foundation/Runtime/SupplyTables.cs Assets/Foundation/Runtime/RoomModel.cs tools/FoundationSmokeTests.cs
${FOUNDATION_MONO:-mono} "$test_dir/foundation-tests.exe"
