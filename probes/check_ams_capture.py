"""Check a real AMS capture for the required playback-state and control evidence."""

import argparse
import json
import math
from pathlib import Path


def check_capture(report: dict) -> dict:
    attributes = {
        (row["entityId"], row["attributeId"]): row["value"]
        for row in report["attributes"]
        if row.get("readStatus") == "Success"
    }
    current_title = attributes[(2, 2)]
    initial_title = current_title
    previous_playback = None
    paused_at = None
    pause_durations = []
    seek_residuals = []
    title_changes = []
    frequency = report["timestampFrequency"]

    for event in report["events"]:
        pair = event["entityId"], event["attributeId"]
        value = event["value"]
        if event["truncated"]:
            full = event["completeAttribute"]
            if full.get("readStatus") != "Success":
                raise ValueError("A truncated AMS value could not be read completely")
            value = full["value"]

        if pair == (2, 2) and value != current_title:
            title_changes.append({"from": current_title, "to": value})
            current_title = value
            previous_playback = None
            paused_at = None
        elif pair == (0, 1):
            state_text, rate_text, position_text = value.split(",")
            state, rate, position = int(state_text), float(rate_text), float(position_text)
            if state not in (0, 1, 2, 3) or not all(map(math.isfinite, (rate, position))):
                raise ValueError("Invalid AMS playback info")
            timestamp = event["receivedTimestamp"]
            if previous_playback is not None:
                old_state, old_rate, old_position, old_timestamp = previous_playback
                if state == 1 and old_state == 1:
                    predicted = old_position + (timestamp - old_timestamp) / frequency * old_rate
                    residual = position - predicted
                    if abs(residual) > 2:
                        seek_residuals.append(round(residual, 3))
            if state == 0:
                if paused_at is None:
                    paused_at = timestamp
            elif state == 1 and paused_at is not None:
                pause_durations.append(round((timestamp - paused_at) / frequency, 3))
                paused_at = None
            previous_playback = state, rate, position, timestamp

    basic = (
        report["amsConnectionVerified"]
        and attributes[(0, 0)] == "YouTube Music"
        and bool(initial_title)
        and (0, 1) in attributes
    )
    controls = bool(pause_durations and seek_residuals and title_changes)
    return {
        "basicStateReadVerified": basic,
        "controlEvidenceVerified": controls,
        "player": attributes[(0, 0)],
        "initialTitle": initial_title,
        "eventCount": len(report["events"]),
        "pauseResumeDurationsSeconds": pause_durations,
        "progressDiscontinuitiesSeconds": seek_residuals,
        "titleChanges": title_changes,
        "audioAlignmentVerified": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    args = parser.parse_args()
    report = json.loads(args.capture.read_text())
    if report["status"] != "ams-events-received":
        parser.error("Capture did not receive AMS events")
    result = check_capture(report)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["basicStateReadVerified"] and result["controlEvidenceVerified"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
