namespace ProjectCeres.Common.Email;

public sealed record EmailMessage(EmailRecipient To, string Subject, string BodyHtml, string BodyText);
