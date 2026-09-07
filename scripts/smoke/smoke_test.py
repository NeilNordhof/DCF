import json
import os
import queue
import subprocess
import sys
import time
from pathlib import Path

import httpx
import paho.mqtt.client as mqtt_client

API_URL = os.environ.get("SMOKE_API_URL", "http://localhost:5000")
DB_URL = os.environ.get("SMOKE_DB_URL")
MQTT_HOST = os.environ.get("SMOKE_MQTT_HOST", "localhost")
MQTT_PORT = int(os.environ.get("SMOKE_MQTT_PORT", 1883))
SCRIPT_DIR = Path(__file__).parent
CAPTIONS = [0, 3, 8]
# MQTT draft-state payloads serialize Caption via C#'s enum .ToString() (a name), while the
# request DTOs bind the same field from its raw int value - this maps the two representations
# for the one place the smoke test needs to compare them.
CAPTION_NAMES = {
    0: 'GeneralEffectCombined', 3: 'VisualCombined', 8: 'MusicCombined',
}

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

def wait_for_chart(job_id, sub, timeout=15):
    deadline = time.time() + timeout

    while time.time() < deadline:
        resp = api("GET", f"/api/charts/requests/{job_id}", sub)
        assert_status(resp, 200, "Poll chart job")
        job = resp.json()

        if job["status"] in ("Succeeded", "Failed"):
            return job

        time.sleep(0.25)

    raise AssertionError(f"Chart job {job_id} did not complete within {timeout} seconds")

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
            "isExhibition" : False,
            "location" : None,
            "latitude" : None,
            "longitude" : None,
            "corpsIds" : corps_ids,
            "schedule" : []
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
        timer_draft_queue = queue.Queue()
        scores_queue = queue.Queue()
        timer_league_id = None  # set once the pick-timer league below is created
        def on_message(client, userdata, msg):
            if msg.topic == f"dcf/leagues/{league_id}/draft":
                draft_queue.put(msg.payload)
            elif msg.topic == f"dcf/leagues/{timer_league_id}/draft":
                timer_draft_queue.put(msg.payload)
            elif "scores" in msg.topic:
                scores_queue.put(msg.payload)

        mqtt = mqtt_client.Client(mqtt_client.CallbackAPIVersion.VERSION2)
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

        #Run Draft
        draft_state = json.loads(wait_for_message(draft_queue, timeout=5))
        assert draft_state["status"] == "InProgress"

        corps_idx = 0
        user_captions_used = {sub: [] for sub in users}
        skipped_user_3 = False

        while draft_state["status"] == "InProgress":
            drafter_id = draft_state.get("currentDrafterId")
            if not drafter_id:
                break
            sub = id_to_sub.get(drafter_id)
            if not sub:
                break

            if sub == "smoke-user-3" and not skipped_user_3:
                resp = api("POST", f"/api/leagues/{league_id}/draft/skip", "smoke-admin")
                assert_status(resp, 200, "Skip draft for user 3")
                skipped_user_3 = True
                draft_state = json.loads(wait_for_message(draft_queue, timeout=5))
                continue

            corps_pick = corps_ids[corps_idx]
            corps_idx += 1

            caption_pick = next(c for c in CAPTIONS if c not in user_captions_used[sub])
            user_captions_used[sub].append(caption_pick)

            resp = api("POST", f"/api/leagues/{league_id}/draft/pick", sub, json={
                "corpsId": corps_pick,
                "caption" : caption_pick
            })
            assert_status(resp, 200, f"Pick corps for user {sub}")

            draft_state = json.loads(wait_for_message(draft_queue, timeout=5))

        # Make up for the skipped user
        assert skipped_user_3
        makeup_caption = next(c for c in CAPTIONS if c not in user_captions_used["smoke-user-3"])
        user_captions_used["smoke-user-3"].append(makeup_caption)
        resp = api("POST", f"/api/leagues/{league_id}/draft/pick", "smoke-user-3", json={
            "corpsId": corps_ids[corps_idx],
            "caption" : makeup_caption
        })
        assert_status(resp, 200, f"Pick corps for user smoke-user-3")

        # Trigger scores scrape
        resp = api("POST", f"/api/admin/shows/{show_id}/scrape", "smoke-admin")
        assert_status(resp, 200, "Trigger scores scrape")

        # Wait for the scores to be updated
        wait_for_message(scores_queue, timeout=10)

        # Verify the scores have been updated
        resp = api("GET", f"/api/leagues/{league_id}/standings/breakdown", "smoke-admin")
        assert_status(resp, 200, "Get standings breakdown")

        assert any(member["totalScore"] > 0 for member in resp.json())

        # --- Draft pick timer smoke check ---
        # A second, isolated league with a short pick timer: verify an
        # unsubmitted pick actually expires on its own and the draft
        # advances into the makeup pool, end-to-end through the real
        # background timer service and a real MQTT publish (not just the
        # unit-tested scheduling logic in isolation).
        resp = api("POST", "/api/leagues", "smoke-admin", json={
            "name": "Smoke Timer League",
            "isPublic": False,
            "corpsPerCaption": 1,
            "maxPlayers": 4,
            "draftableCaptions": CAPTIONS,
            "draftStartTime": None,
            "pickTimerSeconds": 2
        })
        assert_status(resp, 201, "Create timer league")
        timer_league_id = resp.json().get("id")

        resp = api("GET", f"/api/leagues/{timer_league_id}", "smoke-admin")
        assert_status(resp, 200, "Get timer league")
        timer_invite_code = resp.json().get("inviteCode")

        for i in range(1, 4):
            resp = api("POST", f"/api/leagues/{timer_league_id}/join", f"smoke-user-{i}", json={"inviteCode": timer_invite_code})
            assert_status(resp, 204, f"Join timer league for user {i}")

        mqtt.subscribe(f"dcf/leagues/{timer_league_id}/draft")

        resp = api("POST", f"/api/leagues/{timer_league_id}/draft/open", "smoke-admin")
        assert_status(resp, 204, "Open timer draft")
        wait_for_message(timer_draft_queue, timeout=5)

        resp = api("POST", f"/api/leagues/{timer_league_id}/draft/start", "smoke-admin")
        assert_status(resp, 200, "Start timer draft")

        timer_state = json.loads(wait_for_message(timer_draft_queue, timeout=5))
        assert timer_state["status"] == "InProgress"
        assert timer_state["currentPickNumber"] == 0
        assert timer_state["pickTimerSeconds"] == 2
        assert timer_state["pickDeadline"], "Expected an active pick deadline once the timer draft starts"

        # Deliberately submit nothing for pick 0 and let the 2-second timer expire on its own.
        timer_state = json.loads(wait_for_message(timer_draft_queue, timeout=10))
        assert timer_state["currentPickNumber"] == 1, "Pick timer never expired and advanced the draft"
        assert not any(p["pickNumber"] == 0 for p in timer_state["picks"]), \
            "Expired pick should not have been recorded as a real pick"

        # For pick 1, stage a selection but still submit nothing - the timer should auto-submit
        # that selection on expiry instead of dropping it into the makeup pool.
        drafter_sub = id_to_sub[timer_state["currentDrafterId"]]
        resp = api("POST", f"/api/leagues/{timer_league_id}/draft/select", drafter_sub, json={
            "corpsId": corps_ids[0],
            "caption": CAPTIONS[0]
        })
        assert_status(resp, 204, "Select pick 1 for timer league")

        timer_state = json.loads(wait_for_message(timer_draft_queue, timeout=10))
        assert timer_state["currentPickNumber"] == 2, "Selected pick never got auto-submitted on expiry"
        submitted_pick = next((p for p in timer_state["picks"] if p["pickNumber"] == 1), None)
        assert submitted_pick is not None, "Expired pick with a staged selection should have been recorded as a real pick"
        assert submitted_pick["corpsId"] == corps_ids[0]
        assert submitted_pick["caption"] == CAPTION_NAMES[CAPTIONS[0]]

        # The chart engine advertises its available charts
        resp = api("GET", "/api/charts", "smoke-admin")
        assert_status(resp, 200, "List chart definitions")
        chart_keys = [definition["key"] for definition in resp.json()]
        assert "dci-season-score-progression" in chart_keys, chart_keys
        assert "fantasy-league-caption-breakdown" in chart_keys, chart_keys

        # DCI season score progression chart (async submit + poll)
        resp = api("POST", "/api/charts/requests", "smoke-admin", json={
            "chartKey": "dci-season-score-progression",
            "parameters": {"seasonId": season_id, "corpsIds": corps_ids}
        })
        assert_status(resp, 202, "Submit season score progression chart")
        job = wait_for_chart(resp.json()["jobId"], "smoke-admin")
        assert job["status"] == "Succeeded", job
        assert job["result"]["series"], job
        assert any(point["value"] is not None for series in job["result"]["series"] for point in series["points"]), job

        # Fantasy caption breakdown chart, scoped to the league
        resp = api("POST", "/api/charts/requests", "smoke-admin", json={
            "chartKey": "fantasy-league-caption-breakdown",
            "parameters": {"leagueId": league_id}
        })
        assert_status(resp, 202, "Submit fantasy caption breakdown chart")
        job = wait_for_chart(resp.json()["jobId"], "smoke-admin")
        assert job["status"] == "Succeeded", job
        assert len(job["result"]["series"]) == 4, job

        # A user who is not a member of the league cannot chart it
        resp = api("POST", "/api/auth/me", "smoke-outsider", json={"email": "smoke-outsider@example.com", "displayName": "Smoke Outsider"})
        assert_status(resp, 200, "Register outsider")
        resp = api("POST", "/api/charts/requests", "smoke-outsider", json={
            "chartKey": "fantasy-league-caption-breakdown",
            "parameters": {"leagueId": league_id}
        })
        assert_status(resp, 403, "Outsider blocked from league chart")

    finally:
        # Cleanup our http and mqtt
        if http_server_proc is not None:
            http_server_proc.terminate()
            http_server_proc.wait()

        if mqtt is not None:
            mqtt.loop_stop()
            mqtt.disconnect()

        # Cleanup the database
        subprocess.run(["psql", DB_URL, "-f", str(SCRIPT_DIR / "cleanup.sql")], check=True)

if __name__ == "__main__":
    main()

