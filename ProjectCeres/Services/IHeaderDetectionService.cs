using Microsoft.AspNetCore.Http;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IHeaderDetectionService
{
    Task<HeaderDetectionResult> DetectAsync(IFormFile file);
}
