#!/usr/bin/env python3
import argparse
import os
import shutil
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path

from playwright.sync_api import Browser, Page, sync_playwright
from pygments import highlight
from pygments.formatters import HtmlFormatter
from pygments.lexers import CSharpLexer, TextLexer

SAMPLE = Path(__file__).resolve().parent
REPO = SAMPLE.parents[1]
OUT = SAMPLE / "out"
ASSETS = REPO / "docs" / "assets"
WEB_API = REPO / "samples" / "EmberTrace.WebApi"
WEB_API_URL = "http://localhost:5080"
DOTNET_ARGS = ["-c", "Release", "-p:UseLocalEmberTrace=true"]
DOTNET_ENV = {**os.environ, "UseLocalEmberTrace": "true"}

PERFETTO = "https://ui.perfetto.dev"
SPEEDSCOPE = "https://www.speedscope.app"
WIDTH = 1484
DETAILS_HEIGHT = 272
CODE_WIDTH = 1320

SELECT_SLICE = """
async ([name, index]) => {
    const trace = window.app.trace;
    const rows = await trace.engine.query(
        `select id from slice where name = '${name}' order by ts limit 1 offset ${index}`);
    trace.selection.selectSqlEvent('slice', Number(rows.iter({}).get('id')));
}
"""

EXPAND_TRACKS = """
() => {
    const expand = node => {
        node.expand();
        node.children.forEach(expand);
    };
    window.app.trace.currentWorkspace.children.forEach(expand);
}
"""

CODE_PAGE = """<!doctype html>
<meta charset="utf-8">
<style>
{style}
body {{ margin: 0; }}
.code {{ display: inline-block; min-width: {width}px; box-sizing: border-box; padding: 14px 24px 14px 0; }}
.code pre {{ margin: 0; font: 13px/1.7 "JetBrains Mono", "DejaVu Sans Mono", monospace; }}
.code .linenos {{ display: inline-block; width: 44px; padding-right: 20px; text-align: right; color: #6e7681; }}
</style>
{body}
"""


@dataclass(frozen=True)
class Timeline:
    name: str
    select: tuple[str, int] | None = None


@dataclass(frozen=True)
class Listing:
    name: str
    extension: str


TIMELINES = [
    Timeline("api-tracer-perfetto", ("Sort", 0)),
    Timeline("flows-propagation", ("Queue", 0)),
    Timeline("export-opened", ("IoWait", 0)),
    Timeline("analysis-slice", ("CpuWork", 12)),
    Timeline("getting-started-first-trace", ("IoWait", 0)),
    Timeline("runtime-counters-timeline"),
    Timeline("hosting-request-dump", ("Orders.Slow", 0)),
]

LISTINGS = [
    Listing("usage-instrumentation", ".cs"),
    Listing("generator-generated-code", ".cs"),
    Listing("auto-instrumentation-generated-code", ".cs"),
    Listing("troubleshooting-common", ".txt"),
]

FLAME_GRAPH = "analysis-flame-graph"
REPORTS = ["analysis-slice.txt", "getting-started-first-trace.txt"]


def run_scenarios() -> None:
    subprocess.run(
        ["dotnet", "run", "--project", str(SAMPLE), *DOTNET_ARGS],
        env=DOTNET_ENV, check=True)


def fetch(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=30) as response:
        return response.read()


def wait_until_healthy(server: subprocess.Popen, timeout: float = 180) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if server.poll() is not None:
            raise RuntimeError(f"EmberTrace.WebApi exited with code {server.returncode}")
        try:
            fetch(f"{WEB_API_URL}/health")
            return
        except (urllib.error.URLError, ConnectionError):
            time.sleep(0.5)
    raise TimeoutError("EmberTrace.WebApi did not become healthy")


def record_request_dump() -> None:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(WEB_API), *DOTNET_ARGS],
        cwd=WEB_API, env=DOTNET_ENV, stdout=subprocess.DEVNULL, start_new_session=True)
    try:
        wait_until_healthy(server)
        started = time.monotonic()
        with ThreadPoolExecutor(max_workers=4) as pool:
            list(pool.map(lambda order: fetch(f"{WEB_API_URL}/orders/{order}"), range(1, 25)))
        window = time.monotonic() - started + 0.01
        dump = fetch(f"{WEB_API_URL}/embertrace/dump?window=00:00:{window:06.3f}&format=chrome")
    finally:
        os.killpg(server.pid, signal.SIGINT)
        server.wait(timeout=30)

    target = OUT / "hosting-request-dump.json"
    target.write_bytes(dump)
    print(f"Saved: {target}")


def capture_timeline(browser: Browser, shot: Timeline) -> None:
    page = browser.new_page(viewport={"width": WIDTH, "height": 1400})
    page.goto(PERFETTO, wait_until="networkidle")
    page.get_by_text("OK", exact=True).click()
    page.set_input_files("input[type=file]", str(OUT / f"{shot.name}.json"))
    page.wait_for_selector(".pf-track__canvas")
    page.evaluate("window.waitForPerfettoIdle()")
    page.locator(".pf-sidebar__header .pf-sidebar-button").first.click()
    for close in page.locator(".pf-drawer-panel__tab button").all():
        close.click()
    page.evaluate(EXPAND_TRACKS)
    page.evaluate("window.waitForPerfettoIdle()")

    top = page.locator(".pf-timeline-page").bounding_box()["y"]
    last_track = page.locator(".pf-track").last.bounding_box()
    bottom = last_track["y"] + last_track["height"] + 8

    if shot.select is not None:
        page.evaluate(SELECT_SLICE, list(shot.select))
        page.evaluate("window.waitForPerfettoIdle()")
        handle = page.locator(".pf-drawer-panel__handle").bounding_box()
        page.mouse.move(handle["x"] + handle["width"] / 2, handle["y"] + 2)
        page.mouse.down()
        page.mouse.move(handle["x"] + handle["width"] / 2, bottom + 2, steps=8)
        page.mouse.up()
        page.evaluate("window.waitForPerfettoIdle()")
        bottom += DETAILS_HEIGHT

    save(page, shot.name, {"x": 0, "y": top, "width": WIDTH, "height": bottom - top})
    page.close()


def capture_flame_graph(browser: Browser) -> None:
    page = browser.new_page(viewport={"width": WIDTH, "height": 320})
    page.goto(SPEEDSCOPE, wait_until="networkidle")
    page.set_input_files("input[type=file]", str(OUT / f"{FLAME_GRAPH}.folded"))
    page.get_by_text("Left Heavy").click()
    page.wait_for_timeout(1000)
    save(page, FLAME_GRAPH)
    page.close()


def capture_listing(browser: Browser, listing: Listing) -> None:
    source = (OUT / f"{listing.name}{listing.extension}").read_text(encoding="utf-8-sig")
    lexer = CSharpLexer() if listing.extension == ".cs" else TextLexer()
    formatter = HtmlFormatter(style="github-dark", cssclass="code", linenos="inline")
    body = highlight(source.rstrip(), lexer, formatter)

    page = browser.new_page(viewport={"width": CODE_WIDTH, "height": 600})
    page.set_content(CODE_PAGE.format(width=CODE_WIDTH, style=formatter.get_style_defs(".code"), body=body))
    target = ASSETS / f"{listing.name}.png"
    page.locator(".code").screenshot(path=str(target))
    print(f"Saved: {target}")
    page.close()


def save(page: Page, name: str, clip: dict | None = None) -> None:
    target = ASSETS / f"{name}.png"
    page.screenshot(path=str(target), clip=clip)
    print(f"Saved: {target}")


def copy_reports() -> None:
    for report in REPORTS:
        shutil.copyfile(OUT / report, ASSETS / report)
        print(f"Saved: {ASSETS / report}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Regenerates docs/assets from the sample output.")
    parser.add_argument("--only", help="capture a single asset by name")
    parser.add_argument("--no-run", action="store_true", help="reuse the files already in out/")
    args = parser.parse_args()

    if not args.no_run:
        run_scenarios()
        record_request_dump()

    def wanted(name: str) -> bool:
        return args.only is None or args.only == name

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch()
        for shot in TIMELINES:
            if wanted(shot.name):
                capture_timeline(browser, shot)
        if wanted(FLAME_GRAPH):
            capture_flame_graph(browser)
        for listing in LISTINGS:
            if wanted(listing.name):
                capture_listing(browser, listing)
        browser.close()

    if args.only is None:
        copy_reports()

    return 0


if __name__ == "__main__":
    sys.exit(main())
