using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Standards.Authoring;

public sealed record CandidateValidationTicket(string BaselineDigest, string DraftDigest, string CandidateDigest)
{
    public static CandidateValidationTicket Create(CandidateAuthoringService service, StandardCandidateDraft draft)
    {
        var snapshot = CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));
        var candidate = service.Validate(snapshot);
        return new(snapshot.Baseline.FileSha256, snapshot.ContentDigest, CanonicalJson.Sha256Value(candidate));
    }
    public StandardCatalogue RequireCurrent(CandidateAuthoringService service, StandardCandidateDraft draft)
    {
        var snapshot = CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));
        if (snapshot.Baseline.FileSha256 != BaselineDigest || snapshot.ContentDigest != DraftDigest)
            throw new ConfigurationException("The draft or baseline changed after validation. Validate it again before export.");
        var candidate = service.Validate(snapshot);
        if (CanonicalJson.Sha256Value(candidate) != CandidateDigest)
            throw new ConfigurationException("The current validated candidate differs from the reviewed result. Validate again.");
        return candidate;
    }
}
