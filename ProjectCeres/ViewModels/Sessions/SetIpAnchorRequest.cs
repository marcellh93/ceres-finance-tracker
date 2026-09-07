namespace ProjectCeres.ViewModels.Sessions;

// Body for POST api/sessions/{id}/anchor. Mirrors BlockIpRequest.
public sealed record SetIpAnchorRequest(bool Anchored);
