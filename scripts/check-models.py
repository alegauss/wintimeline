"""WT100: checks every file the model catalog names, without downloading it.

    python scripts/check-models.py [catalog.json]

A HEAD request on a Hugging Face resolve URL answers with the file's LFS SHA-256 in
X-Linked-ETag and its size in X-Linked-Size, so a pinned URL that moved, vanished or changed
is caught without fetching gigabytes. Exits 1 and names each mismatch; the nightly workflow
runs it.
"""

import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

CATALOG = Path(__file__).resolve().parent.parent / "src" / "WinTimeline.Inference" / "Catalog" / "models.json"


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def head(url):
    opener = urllib.request.build_opener(NoRedirect)
    request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "wintimeline-model-check"})
    try:
        with opener.open(request, timeout=60) as response:
            return response.status, response.headers
    except urllib.error.HTTPError as redirect:
        # The 302 to the CDN is the answer: it carries the linked hash and size.
        return redirect.code, redirect.headers


def main():
    catalog = json.loads(Path(sys.argv[1] if len(sys.argv) > 1 else CATALOG).read_text(encoding="utf-8"))
    problems = []
    checked = 0
    for model in catalog["models"]:
        for variant in model["variants"]:
            for file in variant["files"]:
                checked += 1
                status, headers = head(file["url"])
                etag = (headers.get("X-Linked-ETag") or headers.get("ETag") or "").strip('"')
                size = headers.get("X-Linked-Size") or headers.get("Content-Length")
                where = f'{model["id"]}/{variant["id"]}/{file["path"]}'
                if status not in (200, 302):
                    problems.append(f"{where}: HTTP {status}")
                elif etag != file["sha256"]:
                    problems.append(f"{where}: SHA-256 is {etag or 'not given'}, the catalog says {file['sha256']}")
                elif size is None or int(size) != file["size"]:
                    problems.append(f"{where}: size is {size}, the catalog says {file['size']}")

    for problem in problems:
        print(problem)
    print(f"{checked} files checked, {len(problems)} problems.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
