# Seed data notice

## valid-guesses.txt

List of English words accepted as attempts (not as daily answers). Generated from the English
frequency dictionary bundled with the `pyspellchecker` Python package (v0.9.1, MIT license),
filtered to lower-case ASCII words of 4 to 10 letters (98,656 entries).

The upstream word frequencies are derived from OpenSubtitles data (FrequencyWords project,
CC BY-SA 4.0). Keep this attribution if the list is redistributed. The list may contain some
proper nouns or slang written in lower case; it is only used to reject strings that are not words.

## words.json

Initial word bank written for this project (definitions, examples and Spanish translations).
CEFR levels are an editorial approximation and must be validated against an open or licensed
CEFR word list before release.
