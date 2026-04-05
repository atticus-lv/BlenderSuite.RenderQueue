using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.Business.Submission;

public interface ILocalSubmissionHost : IDisposable
{
    SubmissionEndpointInfo? CurrentEndpoint { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
