#!/usr/bin/env python3
"""Local WaterFlex telemetry receiver for bench testing."""

from __future__ import annotations

import argparse
import json
import sqlite3
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def init_db(path: Path) -> None:
    with sqlite3.connect(path) as conn:
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS telemetry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                received_at REAL NOT NULL,
                device_id TEXT,
                firmware TEXT,
                uptime_ms INTEGER,
                distance_mm INTEGER,
                synthetic INTEGER,
                wifi_rssi INTEGER,
                local_ip TEXT,
                payload TEXT NOT NULL
            )
            """
        )


def insert_payload(path: Path, payload: dict) -> None:
    with sqlite3.connect(path) as conn:
        conn.execute(
            """
            INSERT INTO telemetry (
                received_at, device_id, firmware, uptime_ms, distance_mm,
                synthetic, wifi_rssi, local_ip, payload
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                time.time(),
                payload.get("device_id"),
                payload.get("firmware"),
                payload.get("uptime_ms"),
                payload.get("distance_mm"),
                1 if payload.get("synthetic") else 0,
                payload.get("wifi_rssi"),
                payload.get("local_ip"),
                json.dumps(payload, separators=(",", ":")),
            ),
        )


class TelemetryHandler(BaseHTTPRequestHandler):
    db_path: Path

    def do_GET(self) -> None:
        if self.path != "/health":
            self.send_error(404)
            return
        self.send_json(200, {"status": "ok"})

    def do_POST(self) -> None:
        if self.path != "/api/v1/telemetry":
            self.send_error(404)
            return

        length = int(self.headers.get("Content-Length", "0"))
        try:
            payload = json.loads(self.rfile.read(length).decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            self.send_json(400, {"error": "invalid_json"})
            return

        insert_payload(self.db_path, payload)
        print(
            "telemetry",
            f"device={payload.get('device_id')}",
            f"distance={payload.get('distance_mm')}",
            f"synthetic={payload.get('synthetic')}",
            f"rssi={payload.get('wifi_rssi')}",
            flush=True,
        )
        self.send_json(202, {"status": "accepted"})

    def send_json(self, status: int, body: dict) -> None:
        encoded = json.dumps(body).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, format: str, *args: object) -> None:
        return


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--db", default="telemetry.db")
    args = parser.parse_args()

    db_path = Path(args.db).resolve()
    init_db(db_path)
    TelemetryHandler.db_path = db_path

    server = ThreadingHTTPServer((args.host, args.port), TelemetryHandler)
    print(f"listening on http://{args.host}:{args.port}")
    print(f"writing telemetry to {db_path}")
    server.serve_forever()


if __name__ == "__main__":
    main()
