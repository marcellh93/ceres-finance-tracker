Feature: GDPR right to erasure
    A user can request permanent erasure of their account, cancel it within 72 hours
    via an emailed link, or let it execute after the window passes.

    Scenario: A user requests erasure then cancels via the emailed link
        Given a signed-in user with recent re-authentication ready for erasure
        When they request account erasure
        Then the erasure request is accepted with status 202
        And their account is sealed
        And they receive an erasure-initiated email with a cancel link
        When they follow the cancel link
        Then the cancellation succeeds
        And their account is no longer sealed
        And they can sign in normally

    Scenario: A user requests erasure and the worker executes it after 72 hours
        Given a signed-in user with recent re-authentication ready for erasure
        When they request account erasure
        Then the erasure request is accepted with status 202
        When 72 hours pass and the erasure worker runs
        Then their login is refused as erased
        And their financial data is gone
        And their statutory records are anonymised but retained
