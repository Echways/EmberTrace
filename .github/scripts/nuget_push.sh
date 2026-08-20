#!/usr/bin/env bash
set -euo pipefail

: "${NUGET_API_KEY:?NUGET_API_KEY is not set}"

source=https://api.nuget.org/v3/index.json
failed=0

if [ "$#" -eq 0 ]; then
    echo "::error::nuget_push.sh got no packages to push"
    exit 1
fi

for package in "$@"; do
    echo "::group::push ${package##*/}"
    if output=$(dotnet nuget push "$package" \
        --source "$source" \
        --api-key "$NUGET_API_KEY" \
        --skip-duplicate 2>&1); then
        echo "$output"
    else
        echo "$output"
        if grep -qiE 'already exists|conflict|409' <<<"$output"; then
            echo "::notice::${package##*/} is already on nuget.org, skipping"
        else
            echo "::error::failed to push ${package##*/}"
            failed=1
        fi
    fi
    echo "::endgroup::"
done

exit "$failed"
