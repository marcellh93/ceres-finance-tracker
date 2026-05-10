namespace ProjectCeres.Common.Email;

public sealed record EmailMessage(string To, string Subject, string BodyHtml, string BodyText);
