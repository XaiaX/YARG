#!/usr/bin/env python3
# pattern: Imperative Shell
"""Build and archive a macOS nightly to the NAS."""
import sys

from build_linux import main


if __name__ == "__main__":
    sys.exit(main("macos"))
