using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ConfigDict = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>;

namespace Dockerize;

public class DockerService : IDockerService
{
    private readonly ILogger<DockerService> _logger;
    private readonly IMemoryCache _memoryCache;

    #region PROPS

    private static readonly Lazy<ConfigDict> LazyYamlConfig =
        new(() =>
        {
            var yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            return yamlDeserializer.Deserialize<ConfigDict>(
                File.ReadAllText(ServiceConfigPath ??
                                 Path.Combine(Environment.CurrentDirectory, "service-config.yaml")));
        });

    private DockerClient Client { get; init; }

    private ConfigDict YamlConfig => LazyYamlConfig.Value;

    private string Target { get; set; } = null!;

    private string ConfigScheme { get; set; }

    private static string? ServiceConfigPath { get; set; }

    private string Cmd => YamlConfig[ConfigScheme]["arguments"].Replace("{host}", Target);

    private string Image => YamlConfig[ConfigScheme]["image"];

    #endregion PROPS

    public DockerService(ILogger<DockerService> logger, IMemoryCache memoryCache)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        ConfigScheme = "default";
        Client = new DockerClientConfiguration().CreateClient() ??
                 throw new Exception("Failed to create Docker client");
    }

    private readonly ContainerAttachParameters _attachParams = new()
    {
        Stream = true,
        Stderr = false,
        Stdin = false,
        Stdout = true
    };

    public async Task PullImages(IEnumerable<string> images, CancellationToken cancellationToken)
    {
        var progress = new Progress<JSONMessage>();
        progress.ProgressChanged += (_, message) =>
        {
            _logger.LogInformation($"{message.ID} : {message.ProgressMessage}");
        };
        var tasks = images.Select(image => Task.Run(async () =>
        {
            try
            {
                await Client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = image, Tag = "latest" },
                    null,
                    progress, cancellationToken)!;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"failed to pull image {image}");
            }
        }, cancellationToken)).ToList();

        await Task.WhenAll(tasks);
    }

    public async Task<StringBuilder> RunWithOutput(string config, string target, CancellationToken cancellationToken)
    {
        var imageExists = await CheckIfImageExistsAsync(Image, cancellationToken);

        if (!imageExists)
        {
            _logger.LogWarning($"{nameof(Image)} not found locally, downloading...");
            await PullImages(new[] { Image }, cancellationToken);
        }

        Target = target;
        ConfigScheme = config;
        var createConfig = new Config
        {
            Image = Image,
            ArgsEscaped = false,
            AttachStderr = false,
            AttachStdin = false,
            AttachStdout = true,
            Cmd = Cmd.Split(),
        };

        CreateContainerResponse containerResponse = await Client.Containers.CreateContainerAsync(
            new CreateContainerParameters(createConfig), cancellationToken);


        var buffer = new StringBuilder();
        var id = containerResponse.ID;
        if (string.IsNullOrEmpty(id))
        {
            _logger.LogError("containerResponse.ID is null or empty...");
            return buffer;
        }

        await using Stream stdOutput = new MemoryStream();
        await using Stream stdError = new MemoryStream();
        try
        {
            using var stream = await Client.Containers.AttachContainerAsync(id, false,
                _attachParams, cancellationToken);

            var streamTask = stream.CopyOutputToAsync(Stream.Null, stdOutput, stdError, cancellationToken);

            if (!await Client.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken))
            {
                throw new Exception("The container has already started.");
            }

            await streamTask;

            //print out errors
            stdError.Seek(0, SeekOrigin.Begin);
            using var sErr = new StreamReader(stdError);
            while (!sErr.EndOfStream)
            {
                var line = await sErr.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line)) continue;
                _logger.LogError($"{line}");
            }

            // grab output and store into buffer
            stdOutput.Seek(0, SeekOrigin.Begin);
            using var sr = new StreamReader(stdOutput);
            while (!sr.EndOfStream)
            {
                var line = await sr.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line)) continue;
                buffer.AppendLine(line);
            }
        }
        finally
        {
            await Client.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters() { Force = true },
                cancellationToken);
        }

        return buffer;
    }

    public async Task<bool> CheckIfImageExistsAsync(string imageName, CancellationToken cancellationToken)
    {
        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            { "reference", new Dictionary<string, bool> { { imageName, true } } }
        };

        IList<ImagesListResponse> images =
            await Client.Images.ListImagesAsync(new ImagesListParameters { Filters = filters }, cancellationToken);


        return images.Any();
    }
}