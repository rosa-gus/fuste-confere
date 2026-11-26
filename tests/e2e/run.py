#!/usr/bin/env python3
"""HTTP integration checks using an isolated API process and temporary SQLite data.

Build the API and frontend first; see README.md in this directory.
Uses only the Python standard library. Never connects to an existing API.
"""
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import csv
from decimal import Decimal
from io import StringIO
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
        dll = ROOT / "src/Fuste.Api/bin/Debug/net10.0/Fuste.Api.dll"
        check(dll.exists(), "Build the API first: dotnet build src/Fuste.Api")
        env = {**os.environ, "FUSTE_CONFERE_DATA_DIR": self.directory,
               "ASPNETCORE_ENVIRONMENT": "Production"}
        self.process = subprocess.Popen(
            ["dotnet", str(dll), "--urls", self.base],
            cwd=ROOT / "src/Fuste.Api", env=env,
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

    def request(self, method, path, value=None, files=None, status=200, raw=False, fields=None, raw_body=None, content_type="application/json"):
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
            for name, content in (fields or {}).items():
                parts.append((f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n'
                              f'{content}\r\n').encode())
            data = b"".join(parts) + f"--{boundary}--\r\n".encode()
            headers["Content-Type"] = f"multipart/form-data; boundary={boundary}"
        elif value is not None:
            data = json.dumps(value).encode()
            headers["Content-Type"] = "application/json"
        if raw_body is not None:
            data = raw_body
            headers["Content-Type"] = content_type
        try:
            response = urlopen(Request(self.base + path, data=data, headers=headers, method=method), timeout=10)
        except HTTPError as error:
            response = error
        with response:
            body = response.read()
            check(response.status in (status if isinstance(status, tuple) else (status,)),
                  f"{method} {path}: expected HTTP {status}, got {response.status}: {body[:1000]!r}")
        return (response.headers, body) if raw else json.loads(body, parse_float=Decimal) if body else None

    def export(self, reconciliation):
        reconciliation_id = reconciliation["id"]
        headers, body = self.request("GET", f"/api/reconciliations/{reconciliation_id}/export", raw=True)
        check(headers.get_content_type() == "text/csv" and headers.get_content_charset() == "utf-8",
              "Wrong CSV content type")
        check(headers.get_content_disposition() == "attachment"
              and headers.get_filename() == f"conferencia-{reconciliation_id}.csv", "Wrong CSV attachment filename")
        check(body.startswith(b"\xef\xbb\xbf"), "CSV must start with a UTF-8 BOM")
        return body


def read_export(body):
    source = StringIO(body.decode("utf-8-sig"), newline="")
    reader = csv.reader(source, delimiter=";", strict=True)
    check(next(reader) == ["ID da venda", "Data da venda", "Valor bruto", "Comissão (%)",
                           "Comissão esperada", "Comissão informada", "Diferença", "Situação"], "Wrong CSV header")
    return list(reader)


def run(api, passed):
    def done(name):
        passed.append(name)
        print(f"PASS {name}", flush=True)

    _, html = api.request("GET", "/", raw=True)
    check(b'<div id="app">' in html and b"/assets/" in html, "Built frontend not served")
    check(b"<title>Fuste</title>" in html and api.request("GET", "/health")["application"] == "Fuste",
          "Frontend or API still reports the former product name")
    check((Path(api.directory) / "fuste-confere.db").is_file(), "Fresh installation did not use the new database filename")
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

    monthly_csv = api.export(result)
    monthly_rows = read_export(monthly_csv)
    check(len(monthly_rows) == 53, "Monthly CSV must have 53 data rows")
    labels = {"Matched": "Conferido", "Difference": "Divergência",
              "MissingFromStatement": "Sem registro no demonstrativo",
              "UnrecognizedSale": "Venda não localizada", "MissingAgreement": "Sem acordo vigente"}
    for row, line in zip(monthly_rows, lines, strict=True):
        check(len(row) == 8 and row[0] == line["saleExternalId"], "CSV identifiers or ordering changed")
        date = "/".join(reversed(line["saleDate"].split("-"))) if line["saleDate"] else ""
        check(row[1] == date and row[7] == labels[line["status"]], f"Wrong date or status: {row[0]}")
        for index, field in [(2, "grossAmount"), (4, "expectedCommission"), (5, "reportedCommission"), (6, "difference")]:
            value = line[field]
            expected_value = f"{value:.2f}".replace(".", ",") if value is not None else ""
            check(row[index] == expected_value, f"Wrong CSV {field}: {row[0]}")
        check((Decimal(row[3].replace(",", ".")) if row[3] else None) == line["commissionPercentage"],
              f"Wrong CSV percentage: {row[0]}")
    configured_csv = api.export(configured)
    check({row[0]: row[1:] for row in read_export(configured_csv)} == {
        "0001": ["01/03/2026", "1000,00", "5", "50,00", "50,00", "0,00", "Conferido"],
        "0002": ["16/03/2026", "200,00", "5,5", "11,00", "10,00", "1,00", "Divergência"],
        "0003": ["17/03/2026", "300,00", "5,5", "16,50", "", "", "Sem registro no demonstrativo"],
        "0004": ["28/02/2026", "100,00", "", "", "", "", "Sem acordo vigente"],
        "0099": ["", "", "", "", "7,00", "", "Venda não localizada"],
    }, "Wrong custom CSV data")
    missing = api.request("GET", f"/api/reconciliations/{uuid.uuid4()}/export", status=404)
    check(missing["detail"] == "A conferência solicitada não foi encontrada.", "Missing CSV error was not localized")
    done("CSV attachment and all values, statuses, ordering, leading-zero IDs and empty fields")

    for name, marker in [("duplicate-sales.csv", "repete o identificador"), ("invalid-date-sales.csv", "data da venda é inválida"),
                         ("invalid-amount-sales.csv", "valor bruto é inválido")]:
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
    check(api.export(result) == monthly_csv and api.export(configured) == configured_csv,
          "Saved exports changed after restart")
    check(api.request("GET", path + "/import-profiles") == profiles, "Profiles changed after restart")
    check(len(api.request("GET", path + "/reconciliations")) == 2, "History changed after restart")
    done("company isolation and exact history/profile preservation after API restart")

    other_path = f'/api/companies/{other["id"]}'
    api.request("POST", other_path + "/agreements", {"effectiveFrom": "2026-03-01", "percentage": 5.123456789})
    complex_id = 'VEN;"A"\nB'
    formula_id = "=1+1"
    with tempfile.TemporaryDirectory(prefix="fuste-e2e-export-") as directory:
        sales, statement = Path(directory) / "sales.csv", Path(directory) / "statement.csv"
        with sales.open("w", encoding="utf-8", newline="") as file:
            csv.writer(file).writerows([
                ["sale_id", "sale_date", "gross_amount"],
                [complex_id, "2026-03-01", "100.00"],
                [formula_id, "2026-03-01", "100.00"],
            ])
        statement.write_text("sale_id,commission_amount\n=1+1,5.12\n", encoding="utf-8")
        special = api.request("POST", other_path + "/reconciliations", files={"sales": sales, "statement": statement})
    special_rows = {row[0]: row for row in read_export(api.export(special))}
    check(set(special_rows) == {complex_id, "'" + formula_id}, "CSV quoting or formula protection failed")
    check(all(row[3] == "5,123456789" for row in special_rows.values()), "CSV rounded a saved percentage")
    done("CSV quoting, formula protection and saved percentage precision")

    # Reimport contract exercised over real HTTP, independently of baseline history.
    repeated_company = api.request("POST", "/api/companies", {"name": "E2E Reimports"})
    repeated_path = f'/api/companies/{repeated_company["id"]}'
    agreement = api.request("POST", repeated_path + "/agreements", {"effectiveFrom": "2026-03-01", "percentage": 5})
    files = {"sales": ROOT / "samples/sales.csv", "statement": ROOT / "samples/statement.csv"}
    initial_fields = {"requestId": str(uuid.uuid4())}
    initial = api.request("POST", repeated_path + "/reconciliations", files=files, fields=initial_fields)
    initial_csv = api.export(initial)
    check(api.request("POST", repeated_path + "/reconciliations", files=files, fields=initial_fields) == initial,
          "Repeated HTTP request did not return its original result")
    conflict = api.request("POST", repeated_path + "/reconciliations", files=files, status=409)
    check(conflict["code"] == "duplicate_import" and conflict["existingReconciliation"]["id"] == initial["id"],
          "Duplicate HTTP response lacks previous reconciliation")
    check(len(api.request("GET", repeated_path + "/reconciliations")) == 1, "Conflict saved extra history")
    with tempfile.TemporaryDirectory(prefix="fuste-e2e-size-") as oversized_dir:
        oversized = Path(oversized_dir) / "oversized.csv"
        oversized.write_bytes(b"x" * (9 * 1024 * 1024 + 1))
        api.request("POST", repeated_path + "/reconciliations",
                    files={"sales": oversized, "statement": files["statement"]}, status=413)
    check(len(api.request("GET", repeated_path + "/reconciliations")) == 1, "Oversized upload wrote history")
    for field in ["requestId", "reprocessOf"]:
        api.request("POST", repeated_path + "/reconciliations", files=files, fields={field: "invalid"}, status=400)
    api.request("POST", repeated_path + "/reconciliations", files=files,
                fields={"reprocessOf": result["id"]}, status=409)
    api.request("DELETE", repeated_path + f'/agreements/{agreement["id"]}', status=204)
    api.request("POST", repeated_path + "/agreements", {"effectiveFrom": "2026-03-01", "percentage": 6})
    api.request("POST", repeated_path + "/reconciliations", files=files, status=409)
    fields = {"requestId": str(uuid.uuid4()), "reprocessOf": initial["id"]}
    repeated = api.request("POST", repeated_path + "/reconciliations", files=files, fields=fields)
    check(repeated["id"] != initial["id"] and repeated["reprocessedFromId"] == initial["id"], "Missing reprocess origin")
    check(next(line for line in repeated["lines"] if line["saleExternalId"] == "VEN-001")["expectedCommission"] == 600,
          "Reprocessing did not use changed agreements")
    check(api.request("GET", f'/api/reconciliations/{initial["id"]}') == initial, "Reprocessing changed prior result")
    api.stop()
    api.start()
    check(api.export(initial) == initial_csv, "Export used changed agreements instead of the saved result")
    check(api.request("POST", repeated_path + "/reconciliations", files=files, fields=fields) == repeated,
          "Confirmed retry after restart created another result")
    mismatch = api.request("POST", repeated_path + "/reconciliations", files=files,
                           fields={"requestId": fields["requestId"]}, status=409)
    check(mismatch["code"] == "request_id_conflict", "Intent mismatch accepted over HTTP")
    api.request("DELETE", repeated_path + f'/reconciliations/{initial["id"]}', status=204)
    retry_after_delete = api.request("POST", repeated_path + "/reconciliations", files=files, fields=fields)
    check(retry_after_delete["id"] == repeated["id"] and retry_after_delete["reprocessedFromId"] is None,
          "Origin deletion broke idempotent retry")
    api.request("POST", repeated_path + "/reconciliations", files=files,
                fields={"requestId": str(uuid.uuid4()), "reprocessOf": initial["id"]}, status=409)
    done("reimport HTTP contract, explicit confirmation, changed agreements and retry after restart/deletion")

    concurrent_company = api.request("POST", "/api/companies", {"name": "E2E Concurrent reimports"})
    concurrent_path = f'/api/companies/{concurrent_company["id"]}/reconciliations'
    with ThreadPoolExecutor(max_workers=4) as executor:
        responses = list(executor.map(lambda _: api.request("POST", concurrent_path, files=files, status=(200, 409)), range(4)))
    check(sum("id" in response for response in responses) == 1
          and sum(response.get("code") == "duplicate_import" for response in responses) == 3,
          "Concurrent HTTP imports created duplicate results")
    origin = next(response["id"] for response in responses if "id" in response)
    concurrent_fields = {"requestId": str(uuid.uuid4()), "reprocessOf": origin}
    with ThreadPoolExecutor(max_workers=4) as executor:
        responses = list(executor.map(lambda _: api.request("POST", concurrent_path, files=files, fields=concurrent_fields), range(4)))
    check(len({response["id"] for response in responses}) == 1 and len(api.request("GET", concurrent_path)) == 2,
          "Concurrent confirmed HTTP retries created duplicate results")
    done("concurrent normal imports and confirmed HTTP retries save only one result per intention")

    # User-visible response text is localized by the API; technical field/code values stay stable.
    invalid_company = api.request("POST", "/api/companies", {"name": ""}, status=400)
    check(invalid_company["title"] == "Dados inválidos" and invalid_company["detail"] == "Informe o nome da representada.",
          "Company validation was not localized")
    invalid_agreement = api.request("POST", repeated_path + "/agreements",
                                    {"effectiveFrom": "2026-01-01", "percentage": 101}, status=400)
    check(invalid_agreement["detail"] == "O percentual deve estar entre 0 e 100.",
          "Domain error leaked an English parameter suffix")
    for body in [b"{", b'{"percentage":"invalid","effectiveFrom":"2026-01-01"}']:
        malformed = api.request("POST", repeated_path + "/agreements", raw_body=body, status=400)
        check(malformed["title"] == "Dados inválidos" and malformed["detail"].startswith("Confira os dados enviados."),
              "Framework binding error was not localized")
    unsupported = api.request("POST", "/api/companies", raw_body=b"text", content_type="text/plain", status=415)
    check(unsupported["title"] == "Formato não suportado", "Unsupported media title was not localized")
    for missing in [f"/api/reconciliations/{uuid.uuid4()}", "/api/unknown-resource"]:
        not_found = api.request("GET", missing, status=404)
        check(not_found["title"] == "Recurso não encontrado" and not_found["detail"] == "O recurso solicitado não foi encontrado.",
              "Not-found response was not localized")
    check(conflict["title"] == "Conflito na operação"
          and conflict["detail"] == "Estes arquivos já foram conferidos para esta representada.",
          "Reimport conflict message was not localized")
    check(failure["issues"][0]["code"] == "missing_column"
          and failure["issues"][0]["message"].startswith("A coluna configurada"),
          "Configured import issue was not localized or technical code changed")
    profile_error = api.request("POST", path + "/import-profiles", {"name": "", "configuration": configuration}, status=400)
    check(profile_error["detail"] == "O nome do perfil de importação deve ter entre 1 e 160 caracteres.",
          "Profile validation was not localized")
    done("Portuguese API validation, structured CSV diagnostics, conflict titles and framework binding errors")




def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, help="Write a JSON execution report")
    args = parser.parse_args()
    passed = []
    failure = None
    with tempfile.TemporaryDirectory(prefix="fuste-e2e-") as directory:
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
