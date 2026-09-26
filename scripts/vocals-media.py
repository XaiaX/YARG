#!/usr/bin/env python3
"""Run YARG's Unity Editor vocals probe on synthetic or caller-owned FLAC media."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shlex
import struct
import subprocess
import sys
import tempfile

PROJECT = Path(__file__).resolve().parents[1]
METHOD = "YARG.Tests.VocalsMediaRunner.Run"


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def smoke_pcm(path):
    with path.open("wb") as output:
        for i in range(44100):
            output.write(struct.pack("<f", 0.4 * math.sin(2 * math.pi * 220 * i / 44100)))


def cli_self_test(args):
    """Run both smoke CLI paths against the real Editor; never use a fake Editor."""
    with tempfile.TemporaryDirectory(prefix="yarg-vocals-cli-") as temporary:
        for name, negative in (("positive", False), ("negative", True)):
            output = Path(temporary) / name
            command = [sys.executable, str(Path(__file__).resolve()), "--unity", args.unity,
                       "--smoke", "--output", str(output)]
            if negative:
                command.append("--negative-smoke")
            result = subprocess.run(command, capture_output=True, text=True)
            log = output / "unity.log"
            report = output / "report.json"
            if result.returncode != 0 or not log.is_file() or not log.stat().st_size:
                raise RuntimeError(f"{name} CLI smoke failed: {result.stderr}; log: {log}")
            text = log.read_text(errors="replace")
            if negative:
                if report.exists() or "VOCALS_MEDIA_FAILED" not in text or "PCM SHA256 mismatch" not in text:
                    raise RuntimeError(f"Negative smoke not rejected: {log}")
            elif not report.is_file() or "VOCALS_MEDIA_OK" not in text:
                raise RuntimeError(f"Positive smoke missing fresh report/success log: {log}")
            print(f"Unity CLI {name}: PASS")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", default=os.environ.get("UNITY_EDITOR", "/Applications/Unity/Hub/Editor/6000.3.5f2/Unity.app/Contents/MacOS/Unity"))
    parser.add_argument("--ffmpeg", default="ffmpeg", help="ffmpeg executable for external conversion")
    parser.add_argument("--ffprobe", default="ffprobe", help="ffprobe executable for source metadata")
    parser.add_argument("--media", type=Path, action="append", help="Caller-owned FLAC; repeat for multiple cases")
    parser.add_argument("--provenance", help="Source/license statement, required with --media")
    parser.add_argument("--output", type=Path, help="Persistent output directory for PCM, manifest, log and report")
    parser.add_argument("--chunk", type=int, default=1024)
    parser.add_argument("--sensitivity", type=float, default=1.0)
    parser.add_argument("--perturbation", choices=("noise", "reverb", "cents", "temporal"),
                        help="Optional deterministic runner variant (requires --seed)")
    parser.add_argument("--seed", type=int, help="Deterministic perturbation seed")
    parser.add_argument("--noise-amplitude", type=float, default=0.02)
    parser.add_argument("--reverb-wet", type=float, default=0.2)
    parser.add_argument("--reverb-delay-seconds", type=float, default=0.08)
    parser.add_argument("--cents", type=float, default=50.0)
    parser.add_argument("--shift-seconds", type=float, default=0.08)
    parser.add_argument("--smoke", action="store_true", help="Generate one synthetic 220 Hz case")
    parser.add_argument("--negative-smoke", action="store_true", help="With --smoke, require SHA mismatch rejection")
    parser.add_argument("--cli-self-test", action="store_true", help="Run real Unity positive/negative synthetic CLI smokes")
    args = parser.parse_args()
    if args.cli_self_test:
        if args.smoke or args.media or args.negative_smoke or args.output:
            parser.error("--cli-self-test cannot be combined with media, smoke or output")
        if not Path(args.unity).is_file():
            parser.error("Unity Editor not found")
        return cli_self_test(args)
    if args.smoke == bool(args.media):
        parser.error("specify exactly one of --smoke and --media")
    if args.media and not args.provenance:
        parser.error("--provenance is required with --media")
    if args.negative_smoke and not args.smoke:
        parser.error("--negative-smoke requires --smoke")
    if (args.seed is None) != (args.perturbation is None):
        parser.error("--seed and --perturbation must be supplied together")
    if args.chunk <= 0 or args.chunk > 44100 * 60 or not math.isfinite(args.sensitivity):
        parser.error("chunk must be 1..2646000 and sensitivity finite")
    if not PROJECT.is_dir() or not Path(args.unity).is_file():
        parser.error("Unity project or Editor not found")

    with tempfile.TemporaryDirectory(prefix="yarg-vocals-") as temporary:
        tmp = Path(temporary)
        output_dir = args.output.resolve() if args.output else tmp / "output"
        if args.output and (output_dir == PROJECT.resolve() or PROJECT.resolve() in output_dir.parents):
            parser.error("--output must be outside the game checkout (private PCM and reports must not enter Assets)")
        report = output_dir / "report.json"
        log = output_dir / "unity.log"
        if report.exists() or log.exists():
            raise RuntimeError(f"Existing report or log; choose a fresh directory: {output_dir}")
        output_dir.mkdir(parents=True, exist_ok=True)
        inputs = output_dir if args.output else tmp
        cases = []
        media_paths = [] if args.smoke else [path.resolve() for path in args.media]
        if len(set(media_paths)) != len(media_paths):
            parser.error("--media paths must be distinct")
        for media in media_paths:
            if not media.is_file() or media.suffix.lower() != ".flac":
                parser.error(f"Input must be an existing FLAC: {media}")
        for index, media in enumerate(media_paths or [None]):
            case_id = "synthetic-220hz" if media is None else f"{index + 1}-{media.stem}"
            pcm = inputs / f"{case_id}.f32le"
            if pcm.exists():
                raise RuntimeError(f"Derived PCM already exists; choose fresh output: {pcm}")
            case = {"id": case_id, "pcm": str(pcm), "format": "f32le", "channels": 1,
                    "sampleRate": 44100, "provenance": {
                        "origin": "generated 220 Hz sine" if media is None else str(media),
                        "rights": "synthetic, generated here" if media is None else args.provenance},
                    "sensitivity": args.sensitivity, "chunkSamples": args.chunk}
            if args.perturbation is not None:
                variant = {"type": args.perturbation, "seed": args.seed}
                if args.perturbation == "noise":
                    variant["noiseAmplitude"] = args.noise_amplitude
                elif args.perturbation == "reverb":
                    variant.update(reverbWet=args.reverb_wet, reverbDelaySeconds=args.reverb_delay_seconds)
                elif args.perturbation == "cents":
                    variant["cents"] = args.cents
                else:
                    variant["shiftSeconds"] = args.shift_seconds
                case["perturbation"] = variant
            if media is None:
                smoke_pcm(pcm)
            else:
                probe_command = [args.ffprobe, "-v", "error", "-show_format", "-show_streams", "-of", "json", str(media)]
                probe = json.loads(subprocess.run(probe_command, check=True, capture_output=True, text=True).stdout)
                command = [args.ffmpeg, "-nostdin", "-v", "error", "-y", "-i", str(media),
                           "-map", "0:a:0", "-ac", "1", "-ar", "44100", "-f", "f32le",
                           "-acodec", "pcm_f32le", str(pcm)]
                subprocess.run(command, check=True)
                duration = probe.get("format", {}).get("duration")
                case.update({"sourceSha256": sha256_file(media),
                             "sourceProbe": json.dumps(probe, sort_keys=True),
                             "ffmpegCommand": shlex.join(command)})
                if duration is not None:
                    case["sourceDuration"] = float(duration)
            case["sha256"] = "0" * 64 if args.negative_smoke else sha256_file(pcm)
            cases.append(case)
        manifest = inputs / "manifest.json"
        if manifest.exists():
            raise RuntimeError(f"Manifest already exists; choose fresh output: {manifest}")
        manifest.write_text(json.dumps({"cases": cases}, indent=2) + "\n")
        command = [args.unity, "-batchmode", "-nographics", "-quit", "-projectPath", str(PROJECT),
                   "-logFile", str(log), "-executeMethod", METHOD,
                   "-vocalsManifest", str(manifest), "-vocalsOutput", str(output_dir)]
        result = subprocess.run(command, check=False)
        text = log.read_text(errors="replace") if log.exists() else ""
        if args.negative_smoke:
            if result.returncode == 0 or "VOCALS_MEDIA_FAILED" not in text or "PCM SHA256 mismatch" not in text or "VOCALS_MEDIA_OK" in text or report.is_file():
                raise RuntimeError(f"Negative SHA smoke was not rejected; inspect {log}")
            print(f"Expected SHA rejection observed; log: {log}")
            return 0
        if result.returncode or "VOCALS_MEDIA_OK" not in text or not report.is_file():
            print(text[-12000:], file=sys.stderr)
            raise RuntimeError(f"Unity probe failed (exit {result.returncode}); inspect {log} (pass --output to persist)")
        data = json.loads(report.read_text())
        results = data.get("results", [])
        if len(results) != len(cases) or any(item.get("sampleCount", 0) <= 0 or
                                             item.get("diagnosticCount", 0) <= 0 for item in results):
            raise RuntimeError("Probe produced missing cases, samples or diagnostics")
        by_id = {case["id"]: case for case in cases}
        if len(by_id) != len(cases) or {item.get("id") for item in results} != set(by_id):
            raise RuntimeError("Report case IDs do not match manifest")
        for item in results:
            case = by_id[item["id"]]
            for key in ("sourceSha256", "sourceProbe", "sourceDuration", "ffmpegCommand",
                        "intervals", "perturbation", "chunkSamples", "sensitivity", "pcm"):
                if key in case:
                    item[key] = case[key]
        data["manifestPath"] = str(manifest)
        data["reportPath"] = str(report)
        report.write_text(json.dumps(data, indent=2) + "\n")
        for item in results:
            print(json.dumps({key: item[key] for key in ("id", "sha256", "provenance", "sampleCount",
                                                        "frameCount", "diagnosticCount")}))
        if args.output:
            print(f"Output directory: {output_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
