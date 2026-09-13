Feature: Support ticket lifecycle
    A support ticket's status reflects who holds the conversation.

    Background:
        Given a signed-in user

    Scenario: A user reply keeps the ticket Open
        Given the user has an open support ticket
        When the user replies to the ticket
        Then the ticket status is "Open"

    Scenario: An operator reply moves the ticket to Pending
        Given the user has an open support ticket
        When an operator replies to the ticket setting the status to "Pending"
        Then the ticket status is "Pending"
