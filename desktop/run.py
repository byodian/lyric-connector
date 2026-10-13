"""Start the native Windows desktop lyrics app, or run its targeted checks."""

import argparse
import base64
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys


def literal(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--device-name", default="iPhone")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--test", action="store_true")
    mode.add_argument("--check-lyrics", action="store_true")
    mode.add_argument("--render-icon", action="store_true")
    parser.add_argument("--smoke-seconds", type=int, default=0)
    args = parser.parse_args()
    if args.smoke_seconds and not 10 <= args.smoke_seconds <= 120:
        parser.error("--smoke-seconds must be between 10 and 120")
    executable = shutil.which("powershell.exe")
    if executable is None:
        parser.error("Windows PowerShell is required. Run on Windows or WSL with Windows interop enabled.")

    directory = Path(__file__).resolve().parent
    imports = set()
    units = []
    for filename in ("Core.cs", "SongQuery.cs", "Source.cs", "LyricsStore.cs", "Windows.cs", "SongGlyph.cs", "Tests.cs"):
        source = (directory / filename).read_text(encoding="utf-8")
        imports.update(re.findall(r"^using [\w.]+;\s*$", source, re.MULTILINE))
        units.append(re.sub(r"^using [\w.]+;\s*$", "", source, flags=re.MULTILINE))
    source = "\n".join(sorted(item.strip() for item in imports)) + "\n" + "\n".join(units)
    script = "$ErrorActionPreference = 'Stop'\n$ProgressPreference = 'SilentlyContinue'\n[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n"
    script += "[Console]::WriteLine('Windows PID: ' + $PID)\n"
    script += "$desktopSource = @'\n" + source + "\n'@\n"
    script += r"""
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$runtime = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$parameters = [CodeDom.Compiler.CompilerParameters]::new()
foreach ($name in @('Windows.Foundation', 'Windows.Devices', 'Windows.Storage')) {
    $parameters.ReferencedAssemblies.Add((Join-Path $env:windir ('System32\WinMetadata\' + $name + '.winmd'))) | Out-Null
}
foreach ($name in @('System.dll', 'System.Core.dll', 'System.Runtime.WindowsRuntime.dll')) {
    $parameters.ReferencedAssemblies.Add((Join-Path $runtime $name)) | Out-Null
}
foreach ($name in @('System.Runtime', 'System.Net.Http', 'System.Web.Extensions', 'WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml')) {
    $assembly = [Reflection.Assembly]::LoadWithPartialName($name)
    if ($null -eq $assembly) { throw ('Missing Windows component: ' + $name) }
    $parameters.ReferencedAssemblies.Add($assembly.Location) | Out-Null
}
Add-Type -TypeDefinition $desktopSource -CompilerParameters $parameters
"""
    if args.test:
        fixture = (directory.parent / "docs" / "iphone-youtubemusic-ams-live.json").read_text(encoding="utf-8")
        script += "$fixture = @'\n" + fixture + "\n'@\n[LyricConnector.Tests]::Run($fixture)\n"
    elif args.check_lyrics:
        script += "[LyricConnector.Tests]::CheckProvider()\n"
    elif args.render_icon:
        icon = directory / "assets" / "lyric-connector.ico"
        preview = directory.parent / "docs" / "desktop-icon.png"
        icon_native = str(icon)
        preview_native = str(preview)
        if os.name != "nt":
            icon_native = subprocess.check_output(["wslpath", "-w", icon_native], text=True).strip()
            preview_native = subprocess.check_output(["wslpath", "-w", preview_native], text=True).strip()
        script += "[LyricConnector.SongGlyph]::Save(" + literal(icon_native) + ", " + literal(preview_native) + ")\n"
    else:
        screenshot = directory.parent / "docs" / "desktop-control.png"
        native_path = str(screenshot)
        icon_path = str(directory / "assets" / "lyric-connector.ico")
        if os.name != "nt":
            native_path = subprocess.check_output(["wslpath", "-w", native_path], text=True).strip()
            icon_path = subprocess.check_output(["wslpath", "-w", icon_path], text=True).strip()
        script += "[LyricConnector.Entry]::Run(" + literal(args.device_name) + ", " + str(args.smoke_seconds) + ", " + literal(native_path) + ", " + literal(icon_path) + ")\n"
    bootstrap = "[Console]::InputEncoding = [Text.UTF8Encoding]::new($false); $source = [Console]::In.ReadToEnd(); & ([ScriptBlock]::Create($source))"
    encoded = base64.b64encode(bootstrap.encode("utf-16-le")).decode("ascii")
    command = [executable, "-NoProfile", "-NonInteractive", "-Sta", "-WindowStyle", "Hidden", "-EncodedCommand", encoded]
    with subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT) as process:
        print(f"Windows process: {process.pid}", flush=True)
        process.stdin.write(script.encode("utf-8"))
        process.stdin.close()
        for line in process.stdout:
            sys.stdout.write(line.decode("utf-8-sig", errors="replace"))
            sys.stdout.flush()
        return process.wait()


if __name__ == "__main__":
    raise SystemExit(main())
