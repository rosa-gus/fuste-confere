#!/usr/bin/env python3
"""HTTP integration checks using an isolated API process and temporary SQLite data.

Build the API and frontend first; see README.md in this directory.
Uses only the Python standard library. Never connects to an existing API.
"""
import argparse
from collections import Counter
from decimal import Decimal
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
import uuid


ROOT = Path(__file__).resolve().parents[2]
FIXTURES = Path(__file__).resolve().parent / "fixtures"


def check(condition, message):
    if not condition:
        raise AssertionError(message)


class Api:
    def __init__(self, directory, log):
        # Loopback port chosen by the OS; readiness also verifies process ownership.
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        self.base = f"http://127.0.0.1:{port}"
        self.directory = directory
        self.log = log
        self.process = None

    def start(self):
        dll = ROOT / "src/ProjectConferidor.Api/bin/Debug/net10.0/ProjectConferidor.Api.dll"
        check(dll.exists(), "Build the API first: dotnet build src/ProjectConferidor.Api")
        env = {**os.environ, "PROJECT_CONFERIDOR_DATA_DIR": self.directory,
               "ASPNETCORE_ENVIRONMENT": "Production"}
        self.process = subprocess.Popen(
            ["dotnet", str(dll), "--urls", self.base],
            cwd=ROOT / "src/ProjectConferidor.Api", env=env,
            stdout=self.log, stderr=subprocess.STDOUT,
        )
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            check(self.process.poll() is None, "API stopped during startup; see log below")
            try:
                self.request("GET", "/health")
                time.sleep(0.1)
                check(self.process.poll() is None, "API could not own its HTTP port")
                return
            except (URLError, TimeoutError):
                time.sleep(0.1)
        raise AssertionError("API did not become ready within 30 seconds")

    def stop(self):
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=5)

    def request(self, method, path, value=None, files=None, status=200, raw=False):
        headers = {}
        data = None
        if files is not None:
            boundary = uuid.uuid4().hex
            parts = []
            for name, file in files.items():
                parts.append((f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"; '
                              f'filename="{file.name}"\r\nContent-Type: text/csv\r\n\r\n').encode()
                             + file.read_bytes() + b"\r\n")
            if value is not None:
                parts.append((f'--{boundary}\r\nContent-Disposition: form-data; '
                              'name="configuration"\r\n\r\n').encode()
                             + json.dumps(value).encode() + b"\r\n")
            data = b"".join(parts) + f"--{boundary}--\r\n".encode()
            headers["Content-Type"] = f"multipart/form-data; boundary={boundary}"
        elif value is not None:
            data = json.dumps(value).encode()
            headers["Content-Type"] = "application/json"
        try:
            response = urlopen(Request(self.base + path, data=data, headers=headers, method=method), timeout=10)
        except HTTPError as error:
            response = error
        with response:
            body = response.read()
            check(response.status == status,
                  f"{method} {path}: expected HTTP {status}, got {response.status}: {body[:1000]!r}")
        return body if raw else json.loads(body, parse_float=Decimal) if body else None


def run(api, passed):
    def done(name):
        passed.append(name)
        print(f"PASS {name}", flush=True)

    html = api.request("GET", "/", raw=True)
    check(b'<div id="app">' in html and b"/assets/" in html, "Built frontend not served")
    check(api.request("GET", "/api/companies") == [], "Temporary database must start empty")
    company = api.request("POST", "/api/companies", {"name": "E2E Aurora"})
    other = api.request("POST", "/api/companies", {"name": "E2E Outra empresa"})
    path = f'/api/companies/{company["id"]}'
    for date, percentage in [("2026-03-01", 5), ("2026-03-16", 5.5)]:
        api.request("POST", path + "/agreements", {"effectiveFrom": date, "percentage": percentage})
    done("initial state, frontend delivery, companies and effective agreements")

    example = {"sales": ROOT / "example/sales.csv", "statement": ROOT / "example/statement.csv"}
    result = api.request("POST", path + "/reconciliations", files=example)
    lines = result["lines"]
    check(Counter(line["status"] for line in lines) == {
        "Matched": 36, "Difference": 8, "MissingFromStatement": 4,
        "UnrecognizedSale": 3, "MissingAgreement": 2}, "Example status counts changed")
    for field, expected in [("grossAmount", "558047.01"), ("expectedCommission", "28662.86"),
                            ("reportedCommission", "27936.35"), ("difference", "307.18")]:
        check(sum((line[field] or Decimal(0)) for line in lines) == Decimal(expected),
              f"Example total changed: {field}")
    by_id = {line["saleExternalId"]: line for line in lines}
    for sale, difference in [("0012", "25"), ("0023", "0.01"), ("0030", "-17.25")]:
        check(by_id[f"VEN-202603-{sale}"]["difference"] == Decimal(difference), f"Wrong difference: {sale}")
    check(by_id["VEN-202603-0019"]["reportedCommission"] == 0
          and by_id["VEN-202603-0019"]["status"] == "Difference", "Zero treated as missing")
    done("monthly example: all 53 statuses, four exact totals and signed differences")

    configuration = json.loads((FIXTURES / "configuration.json").read_text())
    mapped = {"sales": FIXTURES / "mapped-sales.csv", "statement": FIXTURES / "mapped-statement.csv"}
    preview = api.request("POST", path + "/imports/preview", configuration, mapped)
    check(preview["isValid"] and preview["validationComplete"], "Custom preview invalid")
    check([source["totalRows"] for source in preview["sources"]] == [4, 4], "Wrong preview row counts")
    check(len(api.request("GET", path + "/reconciliations")) == 1, "Preview wrote history")
    profile = api.request("POST", path + "/import-profiles", {"name": "E2E layout misto", "configuration": configuration})
    profiles = api.request("GET", path + "/import-profiles")
    check(len(profiles) == 1 and profiles[0] == profile, "Profile not saved/reloaded")
    configured = api.request("POST", path + "/reconciliations", profile["configuration"], mapped)
    check(configured["importConfiguration"] == configuration, "Import snapshot changed")
    indexed = {line["saleExternalId"]: line for line in configured["lines"]}
    expected = {"0001": ("Matched", 50, 50, 0), "0002": ("Difference", 11, 10, 1),
                "0003": ("MissingFromStatement", Decimal("16.5"), None, None),
                "0004": ("MissingAgreement", None, None, None), "0099": ("UnrecognizedSale", None, 7, None)}
    check(set(indexed) == set(expected), "Leading-zero IDs lost or extra lines created")
    for sale, values in expected.items():
        check(tuple(indexed[sale][key] for key in ["status", "expectedCommission", "reportedCommission", "difference"]) == values,
              f"Wrong configured line: {sale}")
    done("mapped preview without writes, reusable profile, mixed formats and leading-zero IDs")

    for name, marker in [("duplicate-sales.csv", "duplicate"), ("invalid-date-sales.csv", "date"),
                         ("invalid-amount-sales.csv", "amount")]:
        failure = api.request("POST", path + "/reconciliations",
                              files={"sales": FIXTURES / name, "statement": FIXTURES / "statement.csv"}, status=400)
        check(marker in failure["detail"].lower(), f"Unexpected validation error: {name}")
        check(len(api.request("GET", path + "/reconciliations")) == 2, f"Invalid import wrote history: {name}")
    bad_configuration = json.loads(json.dumps(configuration))
    bad_configuration["sales"]["columns"]["grossAmount"] = "Coluna inexistente"
    invalid_preview = api.request("POST", path + "/imports/preview", bad_configuration, mapped)
    check(not invalid_preview["isValid"] and invalid_preview["totalIssues"] > 0, "Invalid mapping accepted in preview")
    failure = api.request("POST", path + "/reconciliations", bad_configuration, mapped, status=400)
    check(bool(failure.get("issues")), "Configured execution must return structured issues")
    check(len(api.request("GET", path + "/reconciliations")) == 2, "Invalid configuration wrote history")
    done("invalid dates, amounts, duplicate IDs and mappings rejected without partial history")

    check(api.request("GET", f'/api/companies/{other["id"]}/reconciliations') == [], "History leaked between companies")
    api.request("DELETE", f'/api/companies/{other["id"]}/reconciliations/{result["id"]}', status=404)
    check(api.request("GET", f'/api/reconciliations/{result["id"]}') == result, "Stored result changed")
    api.stop()
    api.start()
    check(api.request("GET", f'/api/reconciliations/{result["id"]}') == result, "Monthly snapshot changed after restart")
    check(api.request("GET", f'/api/reconciliations/{configured["id"]}') == configured, "Configured snapshot changed after restart")
    check(api.request("GET", path + "/import-profiles") == profiles, "Profiles changed after restart")
    check(len(api.request("GET", path + "/reconciliations")) == 2, "History changed after restart")
    done("company isolation and exact history/profile preservation after API restart")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, help="Write a JSON execution report")
    args = parser.parse_args()
    passed = []
    failure = None
    with tempfile.TemporaryDirectory(prefix="conferidor-e2e-") as directory:
        with tempfile.TemporaryFile(mode="w+b") as log:
            api = Api(directory, log)
            try:
                api.start()
                run(api, passed)
            except Exception as error:
                failure = str(error)
                print(f"FAIL {failure}", flush=True)
            finally:
                api.stop()
            if failure:
                log.seek(0)
                print(log.read().decode(errors="replace"))
    if args.report:
        args.report.write_text(json.dumps({"passed": passed, "failure": failure,
                                           "scope": "HTTP API + SQLite; browser validation is manual"},
                                          ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return 1 if failure else 0


if __name__ == "__main__":
    raise SystemExit(main())
