using System.Text;
using Docker.DotNet.Models;

namespace Dockerize;

public interface IDockerService
{
    Task PullImages(IEnumerable<string> images, CancellationToken cancellationToken);
    Task<StringBuilder> RunWithOutput(string config, string target, CancellationToken cancellationToken);
    Task<bool> CheckIfImageExistsAsync(string imageName, CancellationToken cancellationToken);
}