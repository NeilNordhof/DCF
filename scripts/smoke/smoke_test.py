import json
import os
import queue
import subprocess
import sys
import time
import uuid
from pathlib import Path

import httpx
import paho.mqtt.client as mqtt_client

API_URL = os.environ.get("SMOKE_API_URL", "http://localhost:5000")
DB_URL = os.environ.get("SMOKE_DB_URL")
MQTT_HOST = os.environ.get("SMOKE_MQTT_HOST", "localhost")
MQTT_PORT = int(os.environ.get("SMOKE_MQTT_PORT", 1883))
SCRIPT_DIR = Path(__file__).parent
CAPTIONS = [0, 3, 8]

def headers(sub):
    return {
        "Authorization": f"Bearer {sub}",
        "Content-Type": "application/json"
    }

def api(method, path, sub, **kwargs):
    return httpx.request(
        method=method,
        url=f"{API_URL}{path}",
        headers=headers(sub),
        timeout=10,
        **kwargs
    )

def assert_status(resp, expected, label):
    assert resp.status_code == expected, f"{label}: Expected {expected}, got {resp.status_code}: {resp.text}"

def wait_for_message(q, timeout=3):
    try:
        return q.get(timeout=timeout)
    except queue.Empty:
        raise AssertionError(f"No MQTT message received within {timeout} seconds")
    
def main():
    if not DB_URL:
        raise RuntimeError("Environment variable 'SMOKE_DB_URL' is required")
    
    http_server_proc = None
    mqtt = None

    try:
        http_server_proc = subprocess.Popen(
            [sys.executable, "-m", "http.server", "8099", "--directory", str(SCRIPT_DIR / "testdata")],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        time.sleep(1)

        # Create admin user and capture the id
        users = {}

        resp = api("POST", "/api/auth/me", "smoke-admin", json={"email": "smoke-admin@example.com", "displayName": "Smoke Admin"})
        assert_status(resp, 200, "Register admin")
        admin_id = resp.json().get("id")

        users["smoke-admin"] = admin_id

        # Elevate the admin user via psql
        subprocess.run(
            ["psql",  DB_URL, "-c",
             "UPDATE \"Users\" SET \"IsAdmin\" = true WHERE \"Auth0Sub\" = 'smoke-admin';"],
            check=True
        )

        # Create the corps and capture their ids
        corps_ids = []

        for i in range(1, 13):
            resp = api("POST", "/api/admin/corps", "smoke-admin", json={"name": f"Smoke Corps {i:02d}"})
            assert_status(resp, 200, f"Create corps {i:02d}")
            corps_ids.append(resp.json().get("id"))

        # Create the season and assign the corps to it
        resp = api("POST", "/api/admin/seasons", "smoke-admin", json={"year" : 9999, "startDate" : "9999-06-01", "endDate" : "9999-08-31"})
        assert_status(resp, 200, "Create season")
        season_id = resp.json().get("id")

        resp = api("PUT", f"/api/admin/seasons/{season_id}/corps", "smoke-admin", json={"corpsIds": corps_ids})
        assert_status(resp, 204, "Assign corps to season")

        # Create a show
        resp = api("POST", f"/api/admin/seasons/{season_id}/shows", "smoke-admin", json={
            "name": "Smoke Show",
            "url" : "http://localhost:8099/sample-recap.html",
            "date" : "9999-06-02",
            "startTime" : None,
            "scoresAnnouncedTime" : "2027-01-01T00:00:00Z",
            "timezone" : None,
            "corpsIds" : corps_ids
            })
        assert_status(resp, 200, "Create show")
        show_id = resp.json().get("id")

        # Publish the season
        resp = api("POST", f"/api/admin/seasons/{season_id}/publish", "smoke-admin")
        assert_status(resp, 204, "Publish season")

        # Create 3 more users
        for i in range(1, 4):
            resp = api("POST", "/api/auth/me", f"smoke-user-{i}", json={"email": f"smoke-user-{i}@example.com", "displayName": f"Smoke User {i}"})
            assert_status(resp, 200, f"Register user {i}")
            users[f"smoke-user-{i}"] = resp.json().get("id")

        id_to_sub = {v: k for k, v in users.items()}

        # Create our league
        resp = api("POST", "/api/leagues", "smoke-admin", json={
            "name": "Smoke League",
            "isPublic": False,
            "corpsPerCaption" : 1,
            "maxPlayers" : 4,
            "draftableCaptions" : CAPTIONS,
            "draftStartTime" : None
            })
        assert_status(resp, 201, "Create league")
        league_id = resp.json().get("id")

        # Get the invite code for the league
        resp = api("GET", f"/api/leagues/{league_id}", "smoke-admin")
        assert_status(resp, 200, "Get league")
        invite_code = resp.json().get("inviteCode")

        # Use the invite code to join the league
        for i in range(1, 4):
            resp = api("POST", f"/api/leagues/{league_id}/join", f"smoke-user-{i}", json={"inviteCode": invite_code})
            assert_status(resp, 204, f"Join league for user {i}")

        # Setup mqtt
        draft_queue = queue.Queue()
        scores_queue = queue.Queue()
        def on_message(client, userdata, msg):
            if "draft" in msg.topic:
                draft_queue.put(msg.payload)
            elif "scores" in msg.topic:
                scores_queue.put(msg.payload)

        mqtt = mqtt_client.Client()
        mqtt.on_message = on_message
        mqtt.connect(MQTT_HOST, MQTT_PORT, 60)
        mqtt.subscribe(f"dcf/leagues/{league_id}/draft")
        mqtt.subscribe("dcf/scores/updated")
        mqtt.loop_start()

        # Open and Start Draft
        resp = api("POST", f"/api/leagues/{league_id}/draft/open", "smoke-admin")
        assert_status(resp, 204, "Open draft")

        wait_for_message(draft_queue, timeout=5)

        resp = api("POST", f"/api/leagues/{league_id}/draft/start", "smoke-admin")
        assert_status(resp, 200, "Start draft")

    finally:
        if http_server_proc:
            http_server_proc.terminate()
            http_server_proc.wait()