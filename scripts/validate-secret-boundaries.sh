#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "::error::$1"
  exit 1
}

tracked_env_files="$(git ls-files | grep -E '(^|/)\.env(\..+)?$' | grep -vE '(^|/)\.env\.example$' || true)"
[[ -z "$tracked_env_files" ]] || fail "A populated .env file is tracked: $tracked_env_files"

tracked_local_settings="$(git ls-files | grep -E '(^|/)appsettings\..*\.local\.json$' || true)"
[[ -z "$tracked_local_settings" ]] || fail "A local appsettings file is tracked: $tracked_local_settings"

tracked_private_keys="$(git ls-files | grep -E '(^|/)[^/]+\.(pem|key|pfx|p12)$' || true)"
[[ -z "$tracked_private_keys" ]] || fail "Private key material is tracked: $tracked_private_keys"

python - <<'PY'
import json
from pathlib import Path

secret_names = {"ApiKey", "SigningKey"}
connection_names = {"DefaultConnection"}

def walk(value, path=()):
    if isinstance(value, dict):
        for key, child in value.items():
            yield from walk(child, path + (key,))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from walk(child, path + (str(index),))
    else:
        yield path, value

for path in Path("src/Conversa.Api").glob("appsettings*.json"):
    data = json.loads(path.read_text(encoding="utf-8"))
    for key_path, value in walk(data):
        key = key_path[-1]
        if key in secret_names and isinstance(value, str) and value.strip():
            if not value.startswith("<"):
                raise SystemExit(
                    f"{path}: {'.'.join(key_path)} contains a non-placeholder secret."
                )

        if key in connection_names and isinstance(value, str) and value.strip():
            if "<" not in value or ">" not in value:
                raise SystemExit(
                    f"{path}: {'.'.join(key_path)} must be empty or use a placeholder."
                )
PY

if grep -nE 'Password=|(^|[[:space:]])(--build-arg|ARG)[^[:space:]]*[^[:space:]]*(KEY|TOKEN|PASSWORD|SECRET)' Dockerfile; then
  fail "Dockerfile contains a credential or secret build argument."
fi

if grep -nE 'echo .*\$(.*(API_KEY|SIGNING_KEY|PASSWORD|SECRET|TOKEN))' .github/workflows/*.yml; then
  fail "A GitHub Actions workflow echoes a secret-bearing environment variable."
fi

for required in '.env' '.env.*' 'appsettings.*.local.json' '**/secrets.json' '**/*.pem' '**/*.key'; do
  grep -Fq "$required" .dockerignore || fail ".dockerignore must exclude $required"
done

echo "Secret boundary validation passed."
