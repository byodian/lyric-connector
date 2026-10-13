"""Probe native Windows Bluetooth capabilities or one named iPhone's AMS."""

import argparse
import base64
from pathlib import Path
import subprocess
import sys


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--probe", choices=("capabilities", "ams"), default="capabilities")
    parser.add_argument("--device-name")
    parser.add_argument("--listen-seconds", type=int, choices=range(1, 601), default=20)
    args = parser.parse_args()
    if args.probe == "ams" and not args.device_name:
        parser.error("--device-name is required for the AMS probe")

    filename = "windows-ams.ps1" if args.probe == "ams" else "windows-bluetooth-capabilities.ps1"
    support = Path(__file__).with_name("winrt.ps1").read_text()
    script = Path(__file__).with_name(filename).read_text()
    source = support + "\n& {\n" + script + "\n}"
    if args.probe == "ams":
        collector = Path(__file__).with_name("NotificationCollector.cs").read_text()
        source = "$NotificationCollectorSource = @'\n" + collector + "\n'@\n" + source
        source += " -DeviceName '" + args.device_name.replace("'", "''") + "'"
        source += " -ListenSeconds " + str(args.listen_seconds)
    bootstrap = (
        "[Console]::InputEncoding = [Text.UTF8Encoding]::new($false); "
        "$source = [Console]::In.ReadToEnd(); "
        "& ([ScriptBlock]::Create($source))"
    )
    encoded = base64.b64encode(bootstrap.encode("utf-16-le")).decode("ascii")
    result = subprocess.run(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
        input=source.encode("utf-8"),
        stdout=subprocess.PIPE,
        timeout=120 + args.listen_seconds if args.probe == "ams" else 45,
    )
    sys.stdout.write(result.stdout.decode("utf-8-sig"))
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
