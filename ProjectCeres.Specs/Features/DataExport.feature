Feature: GDPR data export
    A user can request, receive, and download their personal-data export exactly once.

    Scenario: A user exports their data and downloads it once
        Given a signed-in user with recent re-authentication
        When they request a data export
        Then the request is accepted with status 202
        When the export worker runs
        Then they receive an export-ready email with a download link
        When they follow the link while signed in
        Then the ZIP downloads successfully
        When they follow the same link again
        Then the download is refused as already used
