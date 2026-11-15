# pattern: Functional Core
"""Composition rules for the two Elite Drums release variants."""

from collections import Counter
import re


def overlay(generated, authored):
    """An authored root owns its complete DTA entry and MIDI."""
    entries = {key.casefold(): (key, text, False) for key, text in generated}
    entries.update((key.casefold(), (key, text, True)) for key, text in authored)
    return [entries[key] for key in sorted(entries)]


def checked_omissions(report, allowed):
    """Permit only explicitly named per-song failures, never a failed shared input."""
    if report['status'] == 'published' and not report['errors']:
        return []
    failed = [song for song in report['songs'] if song['outcome'] == 'failed']
    if (report['status'] != 'failed' or not failed
            or Counter(song['error'] for song in failed) != Counter(report['errors'])
            or any(song['id'].casefold() not in allowed for song in failed)):
        raise ValueError('Elite generation did not complete: ' + '\n'.join(report['errors']))
    return sorted({song['id'] for song in failed}, key=str.casefold)


def song_label(authored, base, entry):
    if authored.get('artist') and authored.get('title'):
        return f"{authored['artist']} - {authored['title']}"
    for line in entry.splitlines()[1:]:
        comment = line.lstrip()
        if not comment:
            continue
        if not comment.startswith(';'):
            break
        label = comment[1:].strip()
        if ' - ' in label:
            return label
    if base.get('artist') and base.get('title'):
        return f"{base['artist']} - {base['title']}"
    raise ValueError(f"No Artist - Title metadata for {authored['songId']}")


def format_playlist(charts):
    by_label = {}
    for label, has_extra_kick in charts:
        label = re.sub(r'\s*\(\+\)\s*$', '', ' '.join(label.split()))
        by_label[label] = by_label.get(label, False) or has_extra_kick
    return ''.join(label + (' (+)' if by_label[label] else '') + '\n'
                   for label in sorted(by_label, key=lambda value: (value.casefold(), value)))
