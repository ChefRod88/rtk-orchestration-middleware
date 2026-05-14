#!/usr/bin/env python3
import json
import os
import subprocess
import sys


def find_prompt(value):
    if isinstance(value, dict):
        for key in ("prompt", "text", "message", "input"):
            candidate = value.get(key)
            if isinstance(candidate, str) and candidate.strip():
                return candidate
        for nested in value.values():
            candidate = find_prompt(nested)
            if candidate:
                return candidate
    elif isinstance(value, list):
        for item in value:
            candidate = find_prompt(item)
            if candidate:
                return candidate
    return None


def allow(message=None):
    payload = {"continue": True}
    if message:
        payload["user_message"] = message
    print(json.dumps(payload))


def block(message):
    print(json.dumps({"continue": False, "user_message": message}))


def main():
    try:
        hook_input = json.load(sys.stdin)
    except json.JSONDecodeError:
        allow()
        return

    prompt = find_prompt(hook_input)
    if not prompt:
        allow()
        return

    rtk_path = os.environ.get("RTK_CLI_PATH", "/usr/local/bin/rtk")
    try:
        result = subprocess.run(
            [rtk_path, "--optimize-prompt"],
            input=prompt,
            text=True,
            capture_output=True,
            timeout=float(os.environ.get("RTK_PROMPT_HOOK_TIMEOUT", "8")),
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        allow()
        return

    if result.returncode != 0:
        allow()
        return

    try:
        optimized = json.loads(result.stdout)
    except json.JSONDecodeError:
        allow()
        return

    optimized_prompt = optimized.get("optimized_prompt")
    metrics = optimized.get("metrics") or {}
    if not isinstance(optimized_prompt, str):
        allow()
        return

    original = metrics.get("tokens_original")
    optimized_tokens = metrics.get("tokens_optimized")
    saved = metrics.get("tokens_saved", 0)
    percent = metrics.get("savings_percentage", 0)

    summary = (
        f"R2K prompt tokens: original={original}, optimized={optimized_tokens}, "
        f"saved={saved}, savings={percent}%"
    )

    min_saved = int(os.environ.get("RTK_PROMPT_MIN_SAVED", "1"))
    if saved >= min_saved and optimized_prompt != prompt:
        block(
            summary
            + "\n\nSubmit this optimized prompt instead:\n\n"
            + optimized_prompt
        )
        return

    allow(summary)


if __name__ == "__main__":
    main()
